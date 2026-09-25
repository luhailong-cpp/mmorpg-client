using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MmorpgClient.Game.Role;
using MmorpgClient.UI.Ugui.Role;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Role
{
    /// <summary>
    /// RoleNameRules 是服务端 go/shared/playername 的客户端镜像,这里的用例与 playername_test.go 对照写;
    /// 字符集由 RoleNameCharsetVectors.json(与服务端 testdata/charset_vectors.json 逐字节相同)机械守住。
    /// 设计:服务端仓 docs/design/guild-phase2/03-names.md §3.22「客户端测试」。
    /// </summary>
    public sealed class RoleNameRulesTests
    {
        private const string VectorsPath = "Tests/EditMode/Role/RoleNameCharsetVectors.json";
        private const string EmptyError = "请输入角色名";
        private const string InvalidCharsError = "角色名包含无效字符";
        private const string CharsetError = "角色名仅限汉字、字母与数字";
        private const string TooLongError = "角色名过长";

        // 服务端内置占位词表(playername.builtinSensitiveSubstrings);随机名必须一个都不含。
        private static readonly string[] SensitiveWords = { "管理员", "客服", "官方", "系统", "运营" };

        // ── 规范化 ──────────────────────────────────────────────────────────

        [TestCase(" 云中君 ", "云中君")]
        [TestCase("\u3000云中君\u3000", "云中君")]   // 表意空格经 NFKC 折成空格,再被去掉
        [TestCase("ＡＢ１２", "AB12")]               // 全角字母数字折成半角
        [TestCase("Ab12", "Ab12")]                   // 大小写原样保留;唯一性靠服务端 norm 转小写
        [TestCase("\u3038\u3038", "十十")]           // 杭州码〸经 NFKC 折成正字,折叠后合法
        [TestCase("\uF92C\uF92C", "\u90CE\u90CE")]   // 兼容汉字折成正字「郎」
        [TestCase("\u3007一", "\u3007一")]           // 〇 不在基本区,单独放行
        // 扩展 A:用 Unicode 3.0 就有的 U+3400 / U+4DB5 走完整条规范化;区间边界 U+4DBF(Unicode 13 才分配)
        // 只在向量表里测 IsAllowedCodePoint,不让"运行时的 Unicode 版本"影响这条用例。
        [TestCase("\u3400\u4DB5", "\u3400\u4DB5")]
        public void AcceptsAndNormalizes(string raw, string expected)
        {
            Assert.That(RoleNameRules.TryNormalize(raw, out var display, out var error), Is.True, error);
            Assert.That(display, Is.EqualTo(expected));
            Assert.That(error, Is.Null);

            // 服务端会对收到的名字再规范化一次;客户端发出去的形态必须是它的不动点。
            Assert.That(RoleNameRules.TryNormalize(display, out var again, out _), Is.True);
            Assert.That(again, Is.EqualTo(display));
        }

        [TestCase("云中君!")]
        [TestCase("云中君！")]                         // 全角叹号折成半角,标点仍拒
        [TestCase("云 中君")]                          // 名字内部的空格不去掉,必须拒
        [TestCase("\U0001F600\U0001F600")]             // emoji
        [TestCase("Привет")]                           // 西里尔字母与拉丁同形
        [TestCase("\u00E9\u00E9")]                     // é 不被 NFKC 折成 e
        [TestCase("\u3005\u3005")]                     // 々 不折叠
        [TestCase("\u3021\u3021")]                     // 杭州码〡不折叠
        [TestCase("\u2E80\u2E80")]                     // 部首补充区不折叠
        [TestCase("\uFA0E\uFA0E")]                     // 没有折叠映射的兼容区汉字
        [TestCase("\U00020000\U00020000")]             // 扩展 B 不开放
        [TestCase("\u200B云中君")]                     // 零宽空格:Go 的 TrimSpace 不去它,这里也不能去
        public void RejectsDisallowedCharacters(string raw)
        {
            Assert.That(RoleNameRules.TryNormalize(raw, out var display, out var error), Is.False);
            Assert.That(error, Is.EqualTo(CharsetError));
            Assert.That(display, Is.Empty);
        }

        [Test]
        public void BlankNamesAskForInput()
        {
            // 与 Go strings.TrimSpace 同一张空白表;NFKC 折不掉的 U+0085 / U+1680 / U+2028 / U+2029 也要去掉。
            string[] inputs = { "", "   ", "\u3000\u3000", "\t\r\n", "\u0085\u1680\u2028\u2029", "\u00A0\u202F\u205F" };
            foreach (var raw in inputs)
            {
                Assert.That(RoleNameRules.TryNormalize(raw, out var display, out var error), Is.False, Escape(raw));
                Assert.That(error, Is.EqualTo(EmptyError), Escape(raw));
                Assert.That(display, Is.Empty, Escape(raw));
            }
        }

        [Test]
        public void NullNameAsksForInput()
        {
            Assert.That(RoleNameRules.TryNormalize(null, out var display, out var error), Is.False);
            Assert.That(error, Is.EqualTo(EmptyError));
            Assert.That(display, Is.Empty);
        }

        [Test]
        public void BrokenSurrogatesAreRejectedWithoutThrowing()
        {
            // 写在方法体里而不是 TestCase:特性参数按 UTF-8 存进元数据,孤立代理会被编译器换成 U+FFFD,
            // 那样测到的就不是孤立代理了。
            string[] inputs = { "\uD840", "云\uD840", "\uDC00云", "\uDC00\uD840", "云\uD840中君" };
            foreach (var raw in inputs)
            {
                bool accepted = true;
                string display = null;
                string error = null;
                Assert.DoesNotThrow(() => accepted = RoleNameRules.TryNormalize(raw, out display, out error), Escape(raw));
                Assert.That(accepted, Is.False, Escape(raw));
                Assert.That(error, Is.EqualTo(InvalidCharsError), Escape(raw));
                Assert.That(display, Is.Empty, Escape(raw));
            }

            // 完整的代理对不是"无效字符",它只是不在允许的区间里。
            Assert.That(RoleNameRules.TryNormalize("\uD840\uDC00", out _, out var pairError), Is.False);
            Assert.That(pairError, Is.EqualTo(CharsetError));
        }

        [Test]
        public void StructuralLimitCountsCodePoints()
        {
            Assert.That(RoleNameRules.StructuralMaxChars, Is.EqualTo(32), "与服务端 playername.StructuralMaxRunes 同值");

            Assert.That(RoleNameRules.TryNormalize(new string('云', 32), out var display, out _), Is.True);
            Assert.That(display.Length, Is.EqualTo(32));

            Assert.That(RoleNameRules.TryNormalize(new string('云', 33), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo(TooLongError));

            // 20 个扩展 B 字是 40 个 UTF-16 单元、20 个码点:按码点计没超长,拒绝原因必须是字符集。
            Assert.That(RoleNameRules.TryNormalize(Repeat("\U00020000", 20), out _, out var charsetError), Is.False);
            Assert.That(charsetError, Is.EqualTo(CharsetError));
            Assert.That(RoleNameRules.TryNormalize(Repeat("\U00020000", 33), out _, out var longError), Is.False);
            Assert.That(longError, Is.EqualTo(TooLongError));
        }

        // ── 字符集:与服务端共用向量表 ──────────────────────────────────────

        [Test]
        public void CharsetMatchesServerVectors()
        {
            string path = Path.Combine(Application.dataPath, VectorsPath);
            Assert.That(File.Exists(path), Is.True, path);
            var vectors = JsonUtility.FromJson<CharsetVectors>(File.ReadAllText(path, Encoding.UTF8));

            Assert.That(vectors, Is.Not.Null);
            Assert.That(vectors.source, Does.EndWith("go/shared/playername/testdata/charset_vectors.json"));
            Assert.That(vectors.allowed, Is.Not.Null);
            Assert.That(vectors.allowed, Is.Not.Empty);
            Assert.That(vectors.rejected, Is.Not.Null);
            Assert.That(vectors.rejected, Is.Not.Empty);

            foreach (var v in vectors.allowed)
                Assert.That(RoleNameRules.IsAllowedCodePoint(Convert.ToInt32(v.cp, 16)), Is.True, $"U+{v.cp} 应允许:{v.why}");
            foreach (var v in vectors.rejected)
                Assert.That(RoleNameRules.IsAllowedCodePoint(Convert.ToInt32(v.cp, 16)), Is.False, $"U+{v.cp} 应拒绝:{v.why}");
        }

        // ── 随机名 ──────────────────────────────────────────────────────────

        [Test]
        public void RandomNamesFromOneStreamAreValidAndVaried()
        {
            // 单一随机流:相邻种子的 System.Random 首个输出相关,"每个种子取一个"测不出真实分布。
            var rng = new System.Random(12345);
            var distinct = new HashSet<string>();
            for (int i = 0; i < 100000; i++)
            {
                string name = RoleNameRules.RandomName(rng);
                AssertValidSuggestion(name);
                distinct.Add(name);
            }
            Assert.That(distinct.Count, Is.GreaterThanOrEqualTo(20000));
        }

        [Test]
        public void RandomNameIsDeterministicForTheSameStream()
        {
            var a = new System.Random(7);
            var b = new System.Random(7);
            for (int i = 0; i < 20; i++)
                Assert.That(RoleNameRules.RandomName(a), Is.EqualTo(RoleNameRules.RandomName(b)));
        }

        [Test]
        public void EveryRandomNameCombinationIsValidAndClean()
        {
            // 逐个枚举 RandomName 的全部随机选择,而不是抽样:复姓"上官""东方"与名池拼接会不会
            // 构成敏感词、池子里有没有抄错的重复字,抽样都只能碰运气。
            var rng = new ExhaustiveRandom();
            var distinct = new HashSet<string>();
            int sequences = 0;
            do
            {
                if (++sequences > 2000000) Assert.Fail("选择序列数失控,RandomName 的算法可能变了");
                string name = RoleNameRules.RandomName(rng);
                if (distinct.Add(name)) AssertValidSuggestion(name);
            } while (rng.Advance());

            // 设计 §3.22:姓 80 × (池 A 60 × 池 B 60 + 池 B 60)。池子改了就同步改这里与设计文档。
            Assert.That(distinct.Count, Is.EqualTo(80 * (60 * 60 + 60)));
        }

        [Test]
        public void RandomNameRequiresARandomSource()
        {
            Assert.Throws<ArgumentNullException>(() => RoleNameRules.RandomName(null));
        }

        // ── Tip 文案 ────────────────────────────────────────────────────────

        [Test]
        public void InvalidNameTipShowsServerLengthRange()
        {
            string text = RoleNameRules.TipText((uint)login_error.KRoleNameInvalid, new[] { "2", "12" });
            Assert.That(text, Does.Contain("2–12"));
        }

        [Test]
        public void InvalidNameTipFallsBackWhenParametersAreMissingOrMalformed()
        {
            string generic = RoleNameRules.TipText((uint)login_error.KRoleNameInvalid, null);
            Assert.That(generic, Is.Not.Null.And.Not.Empty);
            Assert.That(RoleNameRules.TipText((uint)login_error.KRoleNameInvalid, new string[0]), Is.EqualTo(generic));
            Assert.That(RoleNameRules.TipText((uint)login_error.KRoleNameInvalid, new[] { "2" }), Is.EqualTo(generic));
            // 参数来自网络:不是纯数字、或上下限颠倒,都不拼进文案。
            Assert.That(RoleNameRules.TipText((uint)login_error.KRoleNameInvalid, new[] { "<b>2</b>", "12" }), Is.EqualTo(generic));
            Assert.That(RoleNameRules.TipText((uint)login_error.KRoleNameInvalid, new[] { "12", "2" }), Is.EqualTo(generic));
            Assert.That(RoleNameRules.TipText((uint)login_error.KRoleNameInvalid, new[] { "0", "12" }), Is.EqualTo(generic));
        }

        [Test]
        public void TakenAndSensitiveTipsHaveText()
        {
            Assert.That(RoleNameRules.TipText((uint)login_error.KRoleNameTaken, null), Is.Not.Null.And.Not.Empty);
            Assert.That(RoleNameRules.TipText((uint)login_error.KRoleNameSensitive, null), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void UnrelatedTipsAreLeftToTheCaller()
        {
            Assert.That(RoleNameRules.TipText(0, null), Is.Null);
            Assert.That(RoleNameRules.TipText((uint)login_error.KLoginInProgress, null), Is.Null);
            Assert.That(RoleNameRules.RetryableCreateHint(0), Is.Null);
            Assert.That(RoleNameRules.RetryableCreateHint((uint)login_error.KRoleNameTaken), Is.Null);
            Assert.That(RoleNameRules.RetryableCreateHint((uint)login_error.KLoginAccountPlayerFull), Is.Null);
        }

        [TestCase(login_error.KLoginDataSerializeFailed)]
        [TestCase(login_error.KLoginInProgress)]
        [TestCase(login_error.KLoginRedisSetFailed)]
        public void RetryableCreateFailuresHaveHint(login_error tip)
        {
            Assert.That(RoleNameRules.RetryableCreateHint((uint)tip), Is.Not.Null.And.Not.Empty);
            // 可重试失败不是名字问题,名字文案不应接管它。
            Assert.That(RoleNameRules.TipText((uint)tip, null), Is.Null);
        }

        // ── 角色卡显示名(RoleFlowUi.DisplayName) ──────────────────────────

        [Test]
        public void RoleCardUsesServerNameWhenPresent()
        {
            var player = new AccountSimplePlayer { PlayerId = 1, ClassId = 1, Gender = 1, Name = "云中君" };
            Assert.That(RoleFlowUi.DisplayName(player), Is.EqualTo("云中君"));
        }

        [Test]
        public void RoleCardFallsBackToCharacterNameWhenNameMissing()
        {
            var unnamed = new AccountSimplePlayer { PlayerId = 1, ClassId = 1, Gender = 1 };
            var blank = new AccountSimplePlayer { PlayerId = 1, ClassId = 1, Gender = 1, Name = "   " };
            string fallback = RoleFlowUi.DisplayName(unnamed);
            Assert.That(string.IsNullOrWhiteSpace(fallback), Is.False);
            Assert.That(RoleFlowUi.DisplayName(blank), Is.EqualTo(fallback));
        }

        // ── 辅助 ────────────────────────────────────────────────────────────

        private static void AssertValidSuggestion(string name)
        {
            // 大循环里不逐条 Assert.That,只在出错时 Fail,免得十万次断言拖慢用例。
            if (!RoleNameRules.TryNormalize(name, out var display, out var error))
                Assert.Fail($"随机名「{name}」没通过预检:{error}");
            if (display != name)
                Assert.Fail($"随机名「{name}」不是规范化不动点:{display}");
            if (name.Length < 2 || name.Length > 4)
                Assert.Fail($"随机名「{name}」字数 {name.Length} 不在 2–4");
            foreach (var word in SensitiveWords)
            {
                if (name.Contains(word))
                    Assert.Fail($"随机名「{name}」含敏感词「{word}」");
            }
        }

        private static string Repeat(string s, int count)
        {
            var sb = new StringBuilder(s.Length * count);
            for (int i = 0; i < count; i++) sb.Append(s);
            return sb.ToString();
        }

        /// <summary>把非 ASCII 字符转成 \uXXXX,孤立代理进失败信息 / 测试结果 XML 不会出问题。</summary>
        private static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c >= 0x20 && c < 0x7F) sb.Append(c);
                else sb.Append("\\u").Append(((int)c).ToString("X4"));
            }
            return sb.ToString();
        }

        [Serializable]
        private sealed class CharsetVectors
        {
            public string source;
            public CodePointVector[] allowed;
            public CodePointVector[] rejected;
        }

        [Serializable]
        private sealed class CodePointVector
        {
            public string cp;  // 十六进制码点,不带 U+
            public string why;
        }

        /// <summary>
        /// 按"里程表"枚举 RandomName 的全部随机选择序列:一次 RandomName 按记录回放已有的选择、
        /// 新位置从 0 开始;调用后 <see cref="Advance"/> 把最后一个还能加一的位置加一并截掉其后(深度优先)。
        /// 序列数多于不同名字数(单字 / 双字由 Next(5)==0 决定,1–4 都走双字),用例按去重后的名字断言。
        /// 只支持 Next(int):设计 §3.22 的算法只用它,用到别的重载说明算法变了,直接失败。
        /// </summary>
        private sealed class ExhaustiveRandom : System.Random
        {
            private readonly List<int> _values = new List<int>();
            private readonly List<int> _limits = new List<int>();
            private int _cursor;

            public override int Next(int maxValue)
            {
                if (maxValue <= 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
                if (_cursor == _values.Count)
                {
                    _values.Add(0);
                    _limits.Add(maxValue);
                }
                else if (_limits[_cursor] != maxValue)
                {
                    throw new InvalidOperationException($"第 {_cursor} 次取随机数的上界变了:{_limits[_cursor]} → {maxValue}");
                }
                return _values[_cursor++];
            }

            public override int Next() => throw new NotSupportedException("RandomName 只应调用 Next(int)");
            public override int Next(int minValue, int maxValue) => throw new NotSupportedException("RandomName 只应调用 Next(int)");
            public override double NextDouble() => throw new NotSupportedException("RandomName 只应调用 Next(int)");

            /// <summary>切到下一个选择序列;全部枚举完返回 false。</summary>
            public bool Advance()
            {
                int used = _cursor;
                _cursor = 0;
                Truncate(used);
                for (int i = used - 1; i >= 0; i--)
                {
                    if (_values[i] + 1 < _limits[i])
                    {
                        _values[i]++;
                        Truncate(i + 1);
                        return true;
                    }
                }
                return false;
            }

            private void Truncate(int length)
            {
                _values.RemoveRange(length, _values.Count - length);
                _limits.RemoveRange(length, _limits.Count - length);
            }
        }
    }
}
