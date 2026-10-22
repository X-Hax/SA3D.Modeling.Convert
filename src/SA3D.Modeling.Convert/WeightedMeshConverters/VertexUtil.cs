using SA3D.Modeling.Structs;
using System.Collections.Generic;
using System.Numerics;

namespace SA3D.Modeling.Convert.WeightedMeshConverters
{
	internal static class VertexUtil
	{
		/// <summary>
		/// Picked off the normal of an icosahedron
		/// </summary>
		private static readonly Vector3 _sspVector3 = new(
			0.7946527600288391f,
			0.5773528218269348f,
			0.18759159743785858f
		);

		/// <summary>
		/// Picked off the normal of a pentagon
		/// </summary>
		private static readonly Vector2 _sspVector2 = new(
			0.951057f,
			0.309017f
		);

		public static float GetVector2SSP(this Vector2 value)
		{
			return Vector2.Dot(value, _sspVector2);
		}

		public static float GetVector3SSP(this Vector3 value)
		{
			return Vector3.Dot(value, _sspVector3);
		}

		public static bool AreNormalsEqual(Vector3 a, Vector3 b)
		{
			return Vector3.Dot(a, b) >= 0.999f;
		}

		public static EqualityComparer<Vector2> CreatePosition2EqualityComparer(float distanceThreshold)
		{
			if(distanceThreshold <= 0)
			{
				return EqualityComparer<Vector2>.Create((a, b) => a.Equals(b));
			}

			float thresholdSquared = distanceThreshold * distanceThreshold;
			return EqualityComparer<Vector2>.Create((a, b) => Vector2.DistanceSquared(a, b) < thresholdSquared);
		}

		public static EqualityComparer<Vector3> CreatePosition3EqualityComparer(float distanceThreshold)
		{
			if(distanceThreshold <= 0)
			{
				return EqualityComparer<Vector3>.Create((a, b) => a.Equals(b));
			}

			float thresholdSquared = distanceThreshold * distanceThreshold;
			return EqualityComparer<Vector3>.Create((a, b) => Vector3.DistanceSquared(a, b) < thresholdSquared);
		}

		public static EqualityComparer<Vector3> CreateNormal3EqualityComparer()
		{
			return EqualityComparer<Vector3>.Create(AreNormalsEqual);
		}

		public static EqualityComparer<Color> CreateByteRGBAColorQualityComparer()
		{
			return EqualityComparer<Color>.Create((a, b) => a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue && a.Alpha == b.Alpha);
		}
	}
}
