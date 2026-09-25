using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MmorpgClient.Game.Role
{
    /// <summary>
    /// 角色名规则的客户端镜像。事实源是服务端 go/shared/playername/playername.go:
    /// 规范化(NFKC → TrimSpace)、按 Unicode 码点计长度、显式区间字符集,三件事与它逐行对照。
    /// 两端字符集由同一份向量表钉住 —— Assets/Tests/EditMode/Role/RoleNameCharsetVectors.json
    /// 与服务端 testdata/charset_vectors.json 逐字节相同,谁改了区间表而没同步,测试就红。
    ///
    /// 只用于**提前提示**,最终裁定永远在服务端(login 按 RoleNameRule 配表把关字数与敏感词,
    /// data_service 再做结构复检)。因此这里刻意不做两件事:
    ///   - 不写玩法字数(2–12):客户端运行期不加载任何配表(RoleNameRuleTableManager 虽已生成,
    ///     工程里没有加载入口、也不随包带数据),字数不对时由服务端 kRoleNameInvalid 的
    ///     parameters=[min_chars,max_chars] 回显,<see cref="TipText"/> 把它拼成文案;
    ///   - 不判敏感词:服务端词表是占位、来源未定(设计 E1),客户端抄一份只会悄悄过时。
    ///
    /// 设计:服务端仓 docs/design/guild-phase2/03-names.md §3.8、§3.22。
    /// </summary>
    public static class RoleNameRules
    {
        /// <summary>
        /// 结构上限(码点数),与服务端 playername.StructuralMaxRunes 同值。它是存储与输入框的物理约束
        /// (player_name.name VARCHAR(64)、输入框 64 个 UTF-16 单元),不是策划可调的玩法数值。
        /// </summary>
        public const int StructuralMaxChars = 32;

        private const string InvalidCharsError = "角色名包含无效字符";
        private const string EmptyError = "请输入角色名";
        private const string CharsetError = "角色名仅限汉字、字母与数字";
        private const string TooLongError = "角色名过长";

        // 允许的非 ASCII 区间,与 Go 的 runeIdeographicZero / runeExtA* / runeBasic* 一一对应。
        // 不用 "是不是汉字" 的通用判断:它会放进部首补充、々、杭州码这类形近冒名字符(理由见 playername.go)。
        private const int IdeographicZero = 0x3007; // 〇:不在基本区里,单独放行
        private const int ExtAFirst = 0x3400;       // CJK 扩展 A 起
        private const int ExtALast = 0x4DBF;        // CJK 扩展 A 止
        private const int BasicFirst = 0x4E00;      // CJK 基本区起
        private const int BasicLast = 0x9FFF;       // CJK 基本区止

        /// <summary>
        /// 单个码点(**已 NFKC 归一化之后**)是否允许出现在角色名里。与 Go 的 IsAllowedRune 同一张表:
        /// ASCII 数字与字母、〇、扩展 A、基本区;扩展 B 起不开放(客户端字体覆盖未核)。
        /// </summary>
        public static bool IsAllowedCodePoint(int cp)
        {
            return (cp >= '0' && cp <= '9') ||
                   (cp >= 'A' && cp <= 'Z') ||
                   (cp >= 'a' && cp <= 'z') ||
                   cp == IdeographicZero ||
                   (cp >= ExtAFirst && cp <= ExtALast) ||
                   (cp >= BasicFirst && cp <= BasicLast);
        }

        /// <summary>
        /// 把输入框里的原始名字归一化并预检。**不抛异常**。
        /// 成功:返回 true,<paramref name="display"/> 为要发给服务端的名字,<paramref name="error"/> 为 null。
        /// 失败:返回 false,<paramref name="display"/> 为空串,<paramref name="error"/> 为可直接显示的提示。
        /// 字数上下限不在这里判(见类注释),只挡结构上限 <see cref="StructuralMaxChars"/>。
        /// </summary>
        public static bool TryNormalize(string raw, out string display, out string error)
        {
            display = string.Empty;
            raw ??= string.Empty;

            // 1. 残缺代理对。C# 字符串是 UTF-16,Go 那边"非法 UTF-8"在这里的对应物就是孤立代理:
            //    输入框按 UTF-16 单元截断,可能把一个 BMP 外的字符切成半个。它必须先于 Normalize 挡住 ——
            //    不同运行时对孤立代理的 Normalize 行为不一致(抛 ArgumentException 或原样放过)。
            if (!IsWellFormedUtf16(raw))
            {
                error = InvalidCharsError;
                return false;
            }

            // 2. NFKC:全角字母数字 → 半角、表意空格 U+3000 → 空格、多数兼容汉字 → 正字。
            string s;
            try
            {
                s = raw.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                error = InvalidCharsError;
                return false;
            }

            // 3. 去首尾空白;空名在正式客户端不允许(建角必填,空名生成只给机器人 / 无界面路径)。
            s = TrimSpace(s);
            if (s.Length == 0)
            {
                error = EmptyError;
                return false;
            }

            // 4. 与 Go 同序:先长度后字符集。长度按码点计,代理对算 1 个字。
            if (CountCodePoints(s) > StructuralMaxChars)
            {
                error = TooLongError;
                return false;
            }

            // 5. 逐码点过字符集。
            for (int i = 0; i < s.Length;)
            {
                if (!TryReadCodePoint(s, ref i, out int cp) || !IsAllowedCodePoint(cp))
                {
                    error = CharsetError;
                    return false;
                }
            }

            display = s;
            error = null;
            return true;
        }

        /// <summary>
        /// 随机生成一个建议名:姓 + 名,2–4 个字,全部落在 CJK 基本区。
        /// 随机源由调用方注入(界面持有一个 System.Random,测试传固定种子)。这里只是"帮玩家想个名字",
        /// 不需要不可预测性:唯一性仍由服务端注册表保证,撞名时服务端回 kRoleNameTaken。
        /// </summary>
        public static string RandomName(System.Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            int pick = rng.Next(SingleSurnames.Length + CompoundSurnames.Length);
            string surname = pick < SingleSurnames.Length
                ? SingleSurnames[pick].ToString()
                : CompoundSurnames[pick - SingleSurnames.Length];

            // 五分之一出单字名(姓 + 池 B),其余出双字名(姓 + 池 A + 池 B)。
            // 单字名只有 80×60 种,给小权重;总组合 80×(60×60+60) = 292,800,两池互不含
            // 管理员 / 客服 / 官方 / 系统 / 运营 任一字,复姓"上官""东方"拼接后也构不成这些词。
            if (rng.Next(5) == 0)
                return surname + GivenNameTail[rng.Next(GivenNameTail.Length)];
            char head = GivenNameHead[rng.Next(GivenNameHead.Length)];
            char tail = GivenNameTail[rng.Next(GivenNameTail.Length)];
            return surname + head + tail;
        }

        /// <summary>
        /// 三个名字 tip 的界面文案;其它 tip 返回 null(调用方继续走原来的失败处理)。
        /// kRoleNameInvalid 带 parameters=[min_chars,max_chars] 时拼出具体字数。
        /// </summary>
        public static string TipText(uint tipId, IList<string> parameters)
        {
            switch (tipId)
            {
                case (uint)login_error.KRoleNameInvalid:
                    return InvalidNameText(parameters);
                case (uint)login_error.KRoleNameTaken:
                    return "该角色名已被使用，请换一个";
                case (uint)login_error.KRoleNameSensitive:
                    return "角色名包含不允许使用的词语，请修改";
                default:
                    return null;
            }
        }

        /// <summary>
        /// 建角时服务端"可重试"的失败(名字登记或写账号没有完成、账号锁被占)对应的提示;其它 tip 返回 null。
        /// 这几种失败下 login 已按 03-names §3.11 释放或保留名字登记,玩家原样再点创建是安全的:
        /// 即便上一次其实已落盘,同名、同职业、同性别的重试会被服务端认作"上次响应丢失"(§3.11 第 6c' 步),
        /// 直接返回已建好的角色。文案不断言"角色未创建":写账号结果未知时服务端自己也不确定。
        /// </summary>
        public static string RetryableCreateHint(uint tipId)
        {
            switch (tipId)
            {
                case (uint)login_error.KLoginDataSerializeFailed:
                case (uint)login_error.KLoginInProgress:
                case (uint)login_error.KLoginRedisSetFailed:
                    return "服务繁忙，请稍后再试创建角色";
                default:
                    return null;
            }
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        private static string InvalidNameText(IList<string> parameters)
        {
            // 参数来自网络:两个都是纯十进制数字、且 1 ≤ min ≤ max 才拼进文案,否则用不带数字的通用文案。
            if (parameters != null && parameters.Count >= 2
                && uint.TryParse(parameters[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint min)
                && uint.TryParse(parameters[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint max)
                && min >= 1 && min <= max)
            {
                return $"角色名需为 {min}–{max} 个字，仅限汉字、字母与数字";
            }
            return "角色名长度或字符不符合要求，仅限汉字、字母与数字";
        }

        private static bool IsWellFormedUtf16(string s)
        {
            for (int i = 0; i < s.Length;)
            {
                if (!TryReadCodePoint(s, ref i, out _)) return false;
            }
            return true;
        }

        private static int CountCodePoints(string s)
        {
            int count = 0;
            for (int i = 0; i < s.Length; count++)
            {
                if (!TryReadCodePoint(s, ref i, out _)) i++; // 调用方已保证无孤立代理;这里只防死循环
            }
            return count;
        }

        /// <summary>
        /// 从 <paramref name="index"/> 读一个码点并前移。遇到孤立代理返回 false 且不前移。
        /// 不用 char.ConvertToUtf32(string, int):它对孤立代理抛异常,而 TryNormalize 承诺不抛。
        /// </summary>
        private static bool TryReadCodePoint(string s, ref int index, out int codePoint)
        {
            char c = s[index];
            if (char.IsHighSurrogate(c))
            {
                if (index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
                {
                    codePoint = char.ConvertToUtf32(c, s[index + 1]);
                    index += 2;
                    return true;
                }
                codePoint = 0;
                return false;
            }
            if (char.IsLowSurrogate(c))
            {
                codePoint = 0;
                return false;
            }
            codePoint = c;
            index++;
            return true;
        }

        private static string TrimSpace(string s)
        {
            int start = 0;
            int end = s.Length;
            while (start < end && IsSpace(s[start])) start++;
            while (end > start && IsSpace(s[end - 1])) end--;
            return start == 0 && end == s.Length ? s : s.Substring(start, end - start);
        }

        /// <summary>
        /// 与 Go strings.TrimSpace 用的 unicode.IsSpace(Unicode White_Space 属性)同一张表。
        /// 不用 string.Trim():它的空白集合跟着运行时的 Unicode 版本走(U+180E 就在版本间进出过空白类),
        /// 镜像要与服务端一字不差。表里全是 BMP 字符,所以按 char 判即可。
        /// </summary>
        private static bool IsSpace(char c)
        {
            return (c >= '\u0009' && c <= '\u000D') ||
                   c == '\u0020' ||
                   c == '\u0085' ||
                   c == '\u00A0' ||
                   c == '\u1680' ||
                   (c >= '\u2000' && c <= '\u200A') ||
                   c == '\u2028' ||
                   c == '\u2029' ||
                   c == '\u202F' ||
                   c == '\u205F' ||
                   c == '\u3000';
        }

        // 姓 80 个(单姓 68 + 复姓 12)、池 A 60 字、池 B 60 字。已脚本核对:各池内无重复、全部在 U+4E00–9FFF、
        // 两池与单姓均不含 管理员客服官方系统运营 任一字;全组合 292,800 个互不相同(测试逐个枚举断言)。
        private const string SingleSurnames =
            "赵钱孙李周吴郑王冯陈褚卫蒋沈韩杨朱秦尤许何吕施张孔曹严华金魏陶姜谢邹喻柏水窦章云苏潘葛范彭郎鲁韦马苗凤花俞任袁柳鲍史唐薛雷贺倪汤殷罗毕郝";

        private static readonly string[] CompoundSurnames =
        {
            "慕容", "上官", "欧阳", "司马", "诸葛", "东方", "南宫", "独孤", "令狐", "皇甫", "公孙", "轩辕",
        };

        private const string GivenNameHead =
            "清玄若无青子长听明星逸紫行问凌云风月雪霜寒江秋春夜晨松竹梅兰书墨琴剑歌羽鸿鹤灵素静安宁远怀知思慕景修承天君如惊落流飞映望";

        private const string GivenNameTail =
            "尘忌衫歌雪河烟舟道霄澜川岚溪泉峰岳林萧瑶璃珏琳瑜璇华辰曦阳光影声心意情仪然真虚空渊宸翎翊珩砚笙箫弦诗棠蘅芷蔚蕴菡萱茗荷萝";
    }
}
