# 28 月兔机关师：独立候选交付审查

本候选位于 candidate-stable-body/28_moon_rabbit_artificer；原 28 目录、正式资源和游戏代码保持原状。状态必须保持 pending visual review，不 seal、不发布。

## 人物与范围

准确角色名为 **月兔机关师**（原目录 STATUS.md）。短直波波头女童，左侧象牙兔簪和红流苏，浅紫交领布工作袍、象牙袖口与围裙、杏色七分裤、青绿布鞋；木工具箱背在背后，左腰木尺与毛笔。材质为普通布、木、竹，无发光、漂浮兔子、魔法粒子或梦幻效果。

仅为 NW 原始 01、02、08 修正近侧解剖 LEFT 手臂到后摆。全部腿部和身体身份保留；另外 56 个已完成步态与 NW 另外 5 个步态未经绘画修改。原图来自内置 image_gen，原生 1254 方图与整画布缩到 443 方格的副本、提示词、被退的 02 第一稿、来源及 QC 均保存在 review/28_nw_arm_fixes；选定来源已复制到 source/provenance/NW-arm-fixes。

## 起步统一

逐方向检查原 01/05 的髋膝归属、前后裤腿遮挡与反侧摆臂，并核对完整周期支撑关系和原提示词，未使用鞋坐标单独推定左右。全部方向现在 canonical 01 为解剖 RIGHT 接触。

| 方向 | 原始第一接触腿 | 完整源格顺序 |
| --- | --- | --- |
| N | LEFT | 05 06 07 08 01 02 03 04 |
| NE | RIGHT | 01 02 03 04 05 06 07 08 |
| E | RIGHT | 01 02 03 04 05 06 07 08 |
| SE | RIGHT | 01 02 03 04 05 06 07 08 |
| S | RIGHT | 01 02 03 04 05 06 07 08 |
| SW | LEFT | 05 06 07 08 01 02 03 04 |
| W | LEFT | 05 06 07 08 01 02 03 04 |
| NW | LEFT | 05 06 07 08 01 02 03 04 |

NW 局部编辑原 01/02/08 因此对应最终 **05/06/04**。整圈只轮转 4 格，不单独交换接触帧、不镜像、不插值。source/phase-plan.json 保存每方向证据、原 sheet 与 prompt SHA；source/cell-source-map.json 保存最终相位到实际原格的逐格映射及像素 SHA；source/source-inventory-sha256.json 保存选定上游文件哈希。

## 组装与比例

source/assemble_28_candidate.py 从原 direction-sources.json 的真实原格重建，按原规则仅整格归一化为 443×443；每格与原已选完成方向帧逐像素核对，确认 **56 个原格全部相同**；NW 原 03/04/05/06/07 同样逐像素相同。idle.png 与 portrait-daoist.png 逐文件字节保留。相位轮转后仍完整保留 64 个独立真实步态和 8 个独立站立。

修正前后 NW 原 01 头顶 37→37 px、头宽 P90 +0.90%；02 头顶 37→36 px、头宽 +0.59%；08 头顶 18→18 px、头宽 0%。未按人物 bbox 适配或拉长腿。三个下半身边界分别只有 01 右边缘减少 1 px，02 和 08 完全不变；腿形经原图并排视觉核对保留。

最终处理使用共同 scale **1.02941176470588**、alignment v3：全帧整数平移到对应方向 idle 头部参考，保留真实脚底深度，不把走路脚强拽到同一屏幕 Y。再现命令入口是 rebuild_candidate.py，它只组装、process、allow-pending-visual verify；不会 seal 或导入游戏。

## 边缘颜色清理

透明底检查可见原发梢有洋红污染。使用现有 edge_despill 的 4 px 边界范围与 12 px 干净颜色参考；该项仅降低受污染边缘的红/蓝通道，保护 alpha、几何、绿色与正常红色。2 px 预览仍有局部污染，所以选用已支持且经过并排视觉核对的 4 px。原始 magenta 原图和所有源格不变；独立检查已将全部 72 张最终帧的 alpha、绿色和边界哈希与清理前逐一核对，全部完全一致。

## 验证与待交付

最终规范相位与 4 px 受限边缘清理版本已完成 process 与独立 verify。结果：64 移动帧、8 独立站立、89 媒体文件；验证状态 passed_exports_pending_visual，manifest/qc 仍为 passed_numeric_qc_pending_visual_review。

- 8 方向每方向均有 8 个唯一真实姿态，逐格上游 SHA 有效，原 56 + NW 5 帧像素全部保留。
- 同方向 body scale CV 最大 2.7253%；跨方向平均高度比 1.06835；idle/walk 高度差最大 6.0090%（E，既有姿态腿形/跨度差）；均通过现有数值门槛。
- v3 同方向头部横向轴最大误差 0 px，头顶定位与对应 idle 一致；脚底深度保留。
- 全 72 张边缘清理前后 alpha、绿色通道与轮廓边界完全相同；修改仅为已有实现允许的边缘红/蓝颜色。
- 最终人工初审已查看 portrait、idle 八向、每向 01/05 与 NW 全八帧。NW 接触、后抬、经过、前伸的支撑腿均反相，近侧摆臂与腿相反；脸型/短发/兔簪/布衣/木箱身份保持，无新增梦幻效果。

详细数值在 processing/candidate-audit.json；独立验证输出在 verification-pending.json。审查图在 processing/candidate-review（contacts-1.png、contacts-2.png、NW-canonical-eight-plus-idle.png、portrait-and-idle-eight.png）；NW-idle-transition.gif 提供站立/步行切换审查。全部正式游戏资源保持原样，尚未 seal 或发布。根任务仍需独立复核最终动态循环及游戏中转向后，再决定正式接入。
