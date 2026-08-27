using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class WorldCoordinateConverterTests
    {
        [TestCase(0f, 0f, 0f)]
        [TestCase(17.25f, 3.5f, 91.75f)]
        [TestCase(400f, 12f, 300f)]
        public void PositionRoundTrip_IsLosslessWithinFloatPrecision(float x, float y, float z)
        {
            var unity = new Vector3(x, y, z);
            var server = WorldCoordinateConverter.UnityToServer(unity);
            var roundTrip = WorldCoordinateConverter.ServerToUnity(server.X, server.Y, server.Z);
            Assert.That(roundTrip, Is.EqualTo(unity));
        }

        [Test]
        public void TianyongBounds_MapToDocumentedServerAxes()
        {
            var unityCorner = new Vector3(
                TianyongMapDefinition.Width,
                0f,
                TianyongMapDefinition.Depth);
            var serverCorner = WorldCoordinateConverter.UnityToServer(unityCorner);

            Assert.That(serverCorner.X, Is.EqualTo(TianyongMapDefinition.Depth));
            Assert.That(serverCorner.Y, Is.EqualTo(TianyongMapDefinition.Width));
            Assert.That(serverCorner.Z, Is.Zero);
        }

        [Test]
        public void ProtobufHelpers_ShareTheSameRoundTripContract()
        {
            var unity = new Vector3(17.25f, 3.5f, 91.75f);

            var location = WorldCoordinateConverter.ToServerLocation(unity);
            var velocity = WorldCoordinateConverter.ToServerVelocity(unity);
            var vector = WorldCoordinateConverter.ToServerVector(unity);

            Assert.That(WorldCoordinateConverter.FromServerLocation(location), Is.EqualTo(unity));
            Assert.That(WorldCoordinateConverter.FromServerVelocity(velocity), Is.EqualTo(unity));
            Assert.That(WorldCoordinateConverter.FromServerVector(vector), Is.EqualTo(unity));

            var rotation = WorldCoordinateConverter.ToServerRotation(unity);
            Assert.That(WorldCoordinateConverter.FromServerRotation(rotation), Is.EqualTo(unity));
        }
    }
}
