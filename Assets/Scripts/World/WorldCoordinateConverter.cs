using UnityEngine;

namespace MmorpgClient.World
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// The single network/world coordinate contract used by scene actors and
    /// movement messages. The server is Z-up (X forward, Y right); Unity is
    /// Y-up (X right, Z forward).
    /// </summary>
    public static class WorldCoordinateConverter
    {
        public readonly struct ServerPosition
        {
            public ServerPosition(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }
        }

        public static Vector3 ServerToUnity(double serverX, double serverY, double serverZ)
            => new((float)serverY, (float)serverZ, (float)serverX);

        public static ServerPosition UnityToServer(Vector3 unity)
            => new(unity.z, unity.x, unity.y);

        public static Vector3 ServerEulerToUnity(double pitch, double yaw, double roll)
            => new((float)yaw, (float)roll, (float)pitch);

        public static ServerPosition UnityEulerToServer(Vector3 unityEuler)
            => new(unityEuler.z, unityEuler.x, unityEuler.y);

        public static global::Location ToServerLocation(Vector3 unity)
        {
            var server = UnityToServer(unity);
            return new global::Location { X = server.X, Y = server.Y, Z = server.Z };
        }

        public static global::Rotation ToServerRotation(Vector3 unityEuler)
        {
            var server = UnityEulerToServer(unityEuler);
            return new global::Rotation { X = server.X, Y = server.Y, Z = server.Z };
        }

        public static global::Velocity ToServerVelocity(Vector3 unity)
        {
            var server = UnityToServer(unity);
            return new global::Velocity { X = server.X, Y = server.Y, Z = server.Z };
        }

        public static global::Vector3 ToServerVector(Vector3 unity)
        {
            var server = UnityToServer(unity);
            return new global::Vector3 { X = server.X, Y = server.Y, Z = server.Z };
        }

        public static Vector3 FromServerLocation(global::Location server)
            => server == null ? Vector3.zero : ServerToUnity(server.X, server.Y, server.Z);

        public static Vector3 FromServerRotation(global::Rotation server)
            => server == null ? Vector3.zero : ServerEulerToUnity(server.X, server.Y, server.Z);

        public static Vector3 FromServerVelocity(global::Velocity server)
            => server == null ? Vector3.zero : ServerToUnity(server.X, server.Y, server.Z);

        public static Vector3 FromServerVector(global::Vector3 server)
            => server == null ? Vector3.zero : ServerToUnity(server.X, server.Y, server.Z);
    }
}
