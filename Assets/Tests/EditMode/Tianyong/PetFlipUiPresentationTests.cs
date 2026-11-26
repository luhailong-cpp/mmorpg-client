using System.Collections.Generic;
using MmorpgClient.Game.PetFlip;
using MmorpgClient.UI.Ugui.PetFlip;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// 灵宠翻牌 UI 展示推导、资源完整性校验、文案口径与横竖屏几何排布单测（纯 C#，零 UnityEngine 运行时依赖）。
    /// </summary>
    public sealed class PetFlipUiPresentationTests
    {
        private static List<PetFlipFaceDescriptor> CreateValidFaces()
        {
            string[] names = { "烛翎", "霜团貂", "桂灯童", "竹风狸", "玄潮龟", "霞角鹿" };
            var list = new List<PetFlipFaceDescriptor>(PetFlipConstants.PairCount);
            for (int i = 0; i < PetFlipConstants.PairCount; i++)
            {
                var pattern = (SpiritPetCardPattern)i;
                list.Add(new PetFlipFaceDescriptor(
                    pattern,
                    PetFlipUiPresentation.GetPetCatalogId(pattern),
                    names[i],
                    new object()));
            }
            return list;
        }

        [Test]
        public void PatternToCatalogId_MatchesDeliveredSixPetMapping()
        {
            Assert.That(PetFlipUiPresentation.GetPetCatalogId(SpiritPetCardPattern.A), Is.EqualTo("01-zhuling"));
            Assert.That(PetFlipUiPresentation.GetPetCatalogId(SpiritPetCardPattern.B), Is.EqualTo("03-shuangtuan"));
            Assert.That(PetFlipUiPresentation.GetPetCatalogId(SpiritPetCardPattern.C), Is.EqualTo("04-guideng"));
            Assert.That(PetFlipUiPresentation.GetPetCatalogId(SpiritPetCardPattern.D), Is.EqualTo("08-zhufengli"));
            Assert.That(PetFlipUiPresentation.GetPetCatalogId(SpiritPetCardPattern.E), Is.EqualTo("10-xuanchaogui"));
            Assert.That(PetFlipUiPresentation.GetPetCatalogId(SpiritPetCardPattern.F), Is.EqualTo("11-xiajiaolu"));
        }

        [Test]
        public void ValidateAssets_RejectsMissingSpritesMissingNamesAndSharedPlaceholder()
        {
            List<PetFlipFaceDescriptor> valid = CreateValidFaces();
            Assert.That(PetFlipUiPresentation.ValidateAssets(true, valid, out string okReason), Is.True);
            Assert.That(okReason, Is.Null);

            Assert.That(PetFlipUiPresentation.ValidateAssets(false, valid, out string coreReason), Is.False);
            Assert.That(coreReason, Is.EqualTo("missing_core_ui_sprites"));

            List<PetFlipFaceDescriptor> missingPortrait = CreateValidFaces();
            missingPortrait[2] = new PetFlipFaceDescriptor(SpiritPetCardPattern.C, "04-guideng", "桂灯童", null);
            Assert.That(PetFlipUiPresentation.ValidateAssets(true, missingPortrait, out string portraitReason), Is.False);
            Assert.That(portraitReason, Is.EqualTo("missing_pet_portrait"));

            List<PetFlipFaceDescriptor> missingName = CreateValidFaces();
            missingName[3] = new PetFlipFaceDescriptor(SpiritPetCardPattern.D, "08-zhufengli", " ", new object());
            Assert.That(PetFlipUiPresentation.ValidateAssets(true, missingName, out string nameReason), Is.False);
            Assert.That(nameReason, Is.EqualTo("missing_pet_name"));

            // 禁止用同一占位图冒充多种灵宠图案开局。
            object sharedPlaceholder = new object();
            List<PetFlipFaceDescriptor> duplicatePlaceholder = CreateValidFaces();
            duplicatePlaceholder[0] = new PetFlipFaceDescriptor(SpiritPetCardPattern.A, "01-zhuling", "烛翎", sharedPlaceholder);
            duplicatePlaceholder[1] = new PetFlipFaceDescriptor(SpiritPetCardPattern.B, "03-shuangtuan", "霜团貂", sharedPlaceholder);
            Assert.That(PetFlipUiPresentation.ValidateAssets(true, duplicatePlaceholder, out string dupReason), Is.False);
            Assert.That(dupReason, Is.EqualTo("duplicate_placeholder_portrait"));
        }

        [Test]
        public void ResolvePage_SkipsEntryWhenUnfinishedBoardExistsAndHonorsLoadStates()
        {
            var store = new InMemoryPetFlipBestRecordStore();
            using var client = PetFlipClient.CreateForTesting(store, () => new[]
            {
                SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C,
                SpiritPetCardPattern.D, SpiritPetCardPattern.E, SpiritPetCardPattern.F,
                SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C,
                SpiritPetCardPattern.D, SpiritPetCardPattern.E, SpiritPetCardPattern.F
            });
            client.NotifySessionRestored(10001, 1);

            PetFlipSnapshot idle = client.GetSnapshot();
            Assert.That(PetFlipUiPresentation.ResolvePage(idle, PetFlipAssetLoadState.Loading), Is.EqualTo(PetFlipUiPage.Loading));
            Assert.That(PetFlipUiPresentation.ResolvePage(idle, PetFlipAssetLoadState.Failed), Is.EqualTo(PetFlipUiPage.LoadFailed));
            Assert.That(PetFlipUiPresentation.ResolvePage(idle, PetFlipAssetLoadState.Ready), Is.EqualTo(PetFlipUiPage.Entry));

            // 开盘并暂关后：重开直接进入牌盘页，不强迫重看说明。
            client.StartOrResume();
            client.FlipCard(0);
            client.Pause(PetFlipInterruptFlags.PanelClosed);
            PetFlipSnapshot pausedBoard = client.GetSnapshot();
            Assert.That(PetFlipUiPresentation.ResolvePage(pausedBoard, PetFlipAssetLoadState.Ready), Is.EqualTo(PetFlipUiPage.Board));
            // 即使资源加载失败，错误页优先显示且内存盘仍保留。
            Assert.That(PetFlipUiPresentation.ResolvePage(pausedBoard, PetFlipAssetLoadState.Failed), Is.EqualTo(PetFlipUiPage.LoadFailed));
            Assert.That(pausedBoard.HasBoard, Is.True);

            // 完成后进入完成结果页。
            client.StartOrResume();
            int[] rest = { 6, 1, 7, 2, 8, 3, 9, 4, 10, 5, 11 };
            for (int i = 0; i < rest.Length; i++) client.FlipCard(rest[i]);
            Assert.That(PetFlipUiPresentation.ResolvePage(client.GetSnapshot(), PetFlipAssetLoadState.Ready), Is.EqualTo(PetFlipUiPage.Complete));
        }

        [Test]
        public void Formatting_MatchesAllFourCompletionResultsCountersAndNotices()
        {
            var noRecord = new PetFlipLocalRecordSnapshot(null, null, PetFlipRecordPersistenceStatus.NoRecord);
            var savedSeven = new PetFlipLocalRecordSnapshot(7, 7, PetFlipRecordPersistenceStatus.Saved);
            Assert.That(PetFlipUiPresentation.FormatBestText(noRecord), Is.EqualTo("暂无纪录"));
            Assert.That(PetFlipUiPresentation.FormatBestText(savedSeven), Is.EqualTo("7 次"));
            Assert.That(PetFlipUiPresentation.FormatPairCounter(2), Is.EqualTo("已配 2/6 对"));
            Assert.That(PetFlipUiPresentation.FormatFlipCounter(5), Is.EqualTo("翻牌次数 5"));

            var first = new PetFlipCompletionResult(1, 11, 6, null, 11, PetFlipRecordComparison.FirstRecord, PetFlipRecordPersistenceStatus.Saved);
            var newBest = new PetFlipCompletionResult(2, 7, 6, 9, 7, PetFlipRecordComparison.NewBest, PetFlipRecordPersistenceStatus.Unsaved);
            var tied = new PetFlipCompletionResult(3, 9, 6, 9, 9, PetFlipRecordComparison.TiedBest, PetFlipRecordPersistenceStatus.Saved);
            var normal = new PetFlipCompletionResult(4, 12, 6, 9, 9, PetFlipRecordComparison.NormalCompletion, PetFlipRecordPersistenceStatus.Saved);

            Assert.That(PetFlipUiPresentation.FormatCompletionHeadline(first), Is.EqualTo("六对齐了！首次纪录11次。"));
            Assert.That(PetFlipUiPresentation.FormatCompletionHeadline(newBest), Is.EqualTo("刷新本机最佳！比上次少2次。"));
            Assert.That(PetFlipUiPresentation.FormatCompletionHeadline(tied), Is.EqualTo("追平本机最佳，9次。"));
            Assert.That(PetFlipUiPresentation.FormatCompletionHeadline(normal), Is.EqualTo("六对齐了！本局12次，本机最佳9次。"));
            Assert.That(PetFlipUiPresentation.FormatCompletionSummary(normal), Is.EqualTo("本局 12 次  ·  本机最佳 9 次"));

            var faceDown = new PetFlipCardSnapshot(0, PetFlipCardState.FaceDown, null, true);
            var faceUp = new PetFlipCardSnapshot(1, PetFlipCardState.FaceUp, SpiritPetCardPattern.B, false);
            var matched = new PetFlipCardSnapshot(2, PetFlipCardState.Matched, SpiritPetCardPattern.C, false);
            Assert.That(PetFlipUiPresentation.FormatCardAccessibleName(faceDown, null), Is.EqualTo("PetFlipCard_0_背面"));
            Assert.That(PetFlipUiPresentation.FormatCardAccessibleName(faceUp, "霜团貂"), Is.EqualTo("PetFlipCard_1_霜团貂_翻开"));
            Assert.That(PetFlipUiPresentation.FormatCardAccessibleName(matched, "桂灯童"), Does.Contain("已配"));
        }

        [TestCase(false, 1922f, 818f)]
        [TestCase(true, 1024f, 1536f)]
        public void OrientationLayout_AllTwelveSlotsAndSixMedallionsAreNonOverlappingAndInBounds(
            bool isPortrait,
            float canvasWidth,
            float canvasHeight)
        {
            var slotRects = new PetFlipPixelRect[PetFlipConstants.BoardCardCount];
            for (int i = 0; i < PetFlipConstants.BoardCardCount; i++)
            {
                PetFlipPixelRect r = PetFlipUiPresentation.GetCardSlotRect(i, isPortrait);
                slotRects[i] = r;
                Assert.That(r.X, Is.GreaterThanOrEqualTo(0f));
                Assert.That(r.Y, Is.GreaterThanOrEqualTo(0f));
                Assert.That(r.X + r.Width, Is.LessThanOrEqualTo(canvasWidth));
                Assert.That(r.Y + r.Height, Is.LessThanOrEqualTo(canvasHeight));
            }

            for (int a = 0; a < slotRects.Length; a++)
            {
                for (int b = a + 1; b < slotRects.Length; b++)
                    Assert.That(Overlaps(slotRects[a], slotRects[b]), Is.False, $"槽位 {a} 与 {b} 重叠");
            }

            var medalRects = new PetFlipPixelRect[PetFlipConstants.PairCount];
            for (int i = 0; i < PetFlipConstants.PairCount; i++)
            {
                PetFlipPixelRect m = PetFlipUiPresentation.GetCompletionMedallionRect(i, isPortrait);
                medalRects[i] = m;
                Assert.That(m.X, Is.GreaterThanOrEqualTo(0f));
                Assert.That(m.X + m.Width, Is.LessThanOrEqualTo(canvasWidth));
                Assert.That(m.Y + m.Height, Is.LessThanOrEqualTo(canvasHeight));
            }

            for (int a = 0; a < medalRects.Length; a++)
            {
                for (int b = a + 1; b < medalRects.Length; b++)
                    Assert.That(Overlaps(medalRects[a], medalRects[b]), Is.False, $"终局头像 {a} 与 {b} 重叠");
            }
        }

        private static bool Overlaps(PetFlipPixelRect a, PetFlipPixelRect b)
            => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

        [Test]
        public void StreakBadgesPetQuotesAndHintPriority_WorkAcrossStates()
        {
            // 6 种灵宠各有非空专属配对小语
            for (int i = 0; i < PetFlipConstants.PairCount; i++)
            {
                string quote = PetFlipUiPresentation.GetPetMatchFlavorText((SpiritPetCardPattern)i);
                Assert.That(string.IsNullOrWhiteSpace(quote), Is.False);
            }

            Assert.That(PetFlipUiPresentation.FormatStreakBadge(0), Is.EqualTo(string.Empty));
            Assert.That(PetFlipUiPresentation.FormatStreakBadge(1), Is.EqualTo(string.Empty));
            Assert.That(PetFlipUiPresentation.FormatStreakBadge(2), Is.EqualTo("二连妙手！"));
            Assert.That(PetFlipUiPresentation.FormatStreakBadge(6), Is.EqualTo("六连一气呵成！"));

            var store = new InMemoryPetFlipBestRecordStore();
            using var client = PetFlipClient.CreateForTesting(store, () => new[]
            {
                SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C,
                SpiritPetCardPattern.D, SpiritPetCardPattern.E, SpiritPetCardPattern.F,
                SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C,
                SpiritPetCardPattern.D, SpiritPetCardPattern.E, SpiritPetCardPattern.F
            });
            client.NotifySessionRestored(10001, 1);
            client.StartOrResume();

            // 第 1 对配中：显示对应灵宠小语
            client.FlipCard(0);
            client.FlipCard(6);
            string afterFirstMatch = PetFlipUiPresentation.FormatBoardHint(client.GetSnapshot(), isPortrait: false);
            Assert.That(afterFirstMatch, Does.Contain("烛翎"));

            // 第 2 对连配：显示二连妙手 + 霜团貂小语
            client.FlipCard(1);
            client.FlipCard(7);
            string afterSecondMatch = PetFlipUiPresentation.FormatBoardHint(client.GetSnapshot(), isPortrait: true);
            Assert.That(afterSecondMatch, Does.Contain("二连妙手"));
            Assert.That(afterSecondMatch, Does.Contain("霜团貂"));

            // 失配优先级高于一切小语，严格显示规范失配文案
            client.FlipCard(2);
            client.FlipCard(3);
            Assert.That(PetFlipUiPresentation.FormatBoardHint(client.GetSnapshot(), isPortrait: true),
                Is.EqualTo(PetFlipUiPresentation.MismatchHintText));

            // 完美局与连击局终局次行汇总
            var perfectResult = new PetFlipCompletionResult(1, 6, 6, null, 6, PetFlipRecordComparison.FirstRecord, PetFlipRecordPersistenceStatus.Saved, maxStreak: 6);
            var streakResult = new PetFlipCompletionResult(2, 8, 6, 6, 6, PetFlipRecordComparison.NormalCompletion, PetFlipRecordPersistenceStatus.Saved, maxStreak: 3);
            Assert.That(PetFlipUiPresentation.FormatCompletionSummary(perfectResult), Is.EqualTo("本局 6 次  ·  本机最佳 6 次  ·  一气呵成"));
            Assert.That(PetFlipUiPresentation.FormatCompletionSummary(streakResult), Is.EqualTo("本局 8 次  ·  本机最佳 6 次  ·  最高 3 连配"));
        }
    }
}
