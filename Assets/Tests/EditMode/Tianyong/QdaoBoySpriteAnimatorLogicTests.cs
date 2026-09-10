using MmorpgClient.World;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// Pure-logic checks for the sprite walker: direction sector selection
    /// (boundary jitter vs. hysteresis), the standing-pose table and the
    /// scale/stride calibration constants. No Unity runtime objects needed.
    /// </summary>
    public sealed class QdaoBoySpriteAnimatorLogicTests
    {
        private const int N = 0, NE = 1, E = 2, S = 4, NW = 7;

        [Test]
        public void SelectDirectionRaw_JittersWhenTheYawHoversOnASectorBoundary()
        {
            // Evidence for the flicker: a heading oscillating by 0.2 deg
            // around the N/NE boundary (22.5) flips the strip every sample.
            var observed = new[]
            {
                QdaoBoySpriteAnimator.SelectDirectionRaw(22.4f),
                QdaoBoySpriteAnimator.SelectDirectionRaw(22.6f),
                QdaoBoySpriteAnimator.SelectDirectionRaw(22.4f),
                QdaoBoySpriteAnimator.SelectDirectionRaw(22.6f),
            };

            Assert.That(observed, Is.EqualTo(new[] { N, NE, N, NE }));
        }

        [Test]
        public void SelectDirection_WithHysteresis_HoldsTheStripThroughBoundaryJitter()
        {
            var direction = N;
            for (var i = 0; i < 20; i++)
            {
                direction = QdaoBoySpriteAnimator.SelectDirection(i % 2 == 0 ? 22.4f : 22.6f, direction);
                Assert.That(direction, Is.EqualTo(N), $"sample {i} must keep N");
            }

            // Symmetric: an actor already on NE stays on NE across the same jitter.
            direction = NE;
            for (var i = 0; i < 20; i++)
            {
                direction = QdaoBoySpriteAnimator.SelectDirection(i % 2 == 0 ? 22.4f : 22.6f, direction);
                Assert.That(direction, Is.EqualTo(NE), $"sample {i} must keep NE");
            }
        }

        [Test]
        public void SelectDirection_LeavesTheSectorOnlyBeyondTheHysteresisBand()
        {
            var band = QdaoBoySpriteAnimator.DirectionSectorDegrees * 0.5f
                       + QdaoBoySpriteAnimator.DirectionHysteresisDegrees; // 27.5

            Assert.That(QdaoBoySpriteAnimator.SelectDirection(band - 0.1f, N), Is.EqualTo(N));
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(band + 0.1f, N), Is.EqualTo(NE));
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(360f - band + 0.1f, N), Is.EqualTo(N));
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(360f - band - 0.1f, N), Is.EqualTo(NW));
        }

        [Test]
        public void SelectDirection_RealTurn_SwitchesOnTheFirstSample()
        {
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(90f, N), Is.EqualTo(E));
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(0f, E), Is.EqualTo(N));
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(180f, N), Is.EqualTo(S), "reversal");
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(0f, S), Is.EqualTo(N), "reversal back");
        }

        [Test]
        public void SelectDirection_HandlesTheZeroThreeSixtyWrap()
        {
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(350f, N), Is.EqualTo(N), "just west of north stays N");
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(337.4f, N), Is.EqualTo(N), "-22.6 is inside the band");
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(5f, NW), Is.EqualTo(N), "NW to just east of north");
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(340f, NW), Is.EqualTo(NW), "NW holds at 340");
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(-10f, N), Is.EqualTo(N), "negative yaw wraps");
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(725f, N), Is.EqualTo(N), "yaw above 360 wraps");
            Assert.That(QdaoBoySpriteAnimator.SelectDirectionRaw(359f), Is.EqualTo(N));
            Assert.That(QdaoBoySpriteAnimator.SelectDirectionRaw(-45f), Is.EqualTo(NW));
        }

        [Test]
        public void SelectDirection_InvalidCurrent_FallsBackToTheNearestSector()
        {
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(22.6f, -1), Is.EqualTo(NE));
            Assert.That(QdaoBoySpriteAnimator.SelectDirection(22.4f, 8), Is.EqualTo(N));
        }

        [Test]
        public void RunHandoffFrames_CoverAllEightDirections()
        {
            // The established grounded run phases are used only for the
            // handoff. Actual Idle rendering uses independent idle_* textures.
            var expected = new[] { 2, 1, 1, 6, 2, 5, 2, 5 };
            var table = QdaoBoySpriteAnimator.IdleFrameTable();

            Assert.That(table.Length, Is.EqualTo(8));
            Assert.That(table, Is.EqualTo(expected));
            for (var d = 0; d < 8; d++)
            {
                Assert.That(QdaoBoySpriteAnimator.IdleFrame(d), Is.EqualTo(expected[d]));
                Assert.That(QdaoBoySpriteAnimator.IdleFrame(d),
                    Is.InRange(0, QdaoBoySpriteAnimator.FramesPerDirection - 1));
            }
        }

        [Test]
        public void ScaleAndStrideCalibration_MatchTheDerivation()
        {
            Assert.That(QdaoBoySpriteAnimator.PixelsPerUnit, Is.EqualTo(52f));
            Assert.That(QdaoBoySpriteAnimator.FrameWorldHeight, Is.EqualTo(512f / 52f).Within(0.001f));
            // cycle distance = 0.5625 x frame height = 5.54 u; 8 frames per cycle.
            Assert.That(QdaoBoySpriteAnimator.CycleWorldDistance, Is.EqualTo(5.54f).Within(0.01f));
            Assert.That(QdaoBoySpriteAnimator.FramesPerWorldUnit, Is.EqualTo(1.444f).Within(0.01f));
            // At the 9 u/s run speed the cycle plays at about 13 fps.
            Assert.That(QdaoBoySpriteAnimator.RunFramesPerSecond, Is.EqualTo(13f).Within(0.1f));
        }
    }
}
