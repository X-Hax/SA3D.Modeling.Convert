using SA3D.Modeling.Structs;
using System;
using System.Numerics;

namespace SA3D.Modeling.Convert.Weighted
{
	/// <summary>
	/// A single corner in a triangle
	/// </summary>
	public struct WeightedCorner : IEquatable<WeightedCorner>
	{
		/// <summary>
		/// Default color for <see cref="Diffuse"/>.
		/// </summary>
		public static readonly Color DefaultDiffuse = Color.ColorWhite;

		/// <summary>
		/// Default color for <see cref="Specular"/>.
		/// </summary>
		public static readonly Color DefaultSpecular = Color.ColorBlack;


		/// <summary>
		/// Vertex index.
		/// </summary>
		public ushort VertexIndex { get; set; }

		/// <summary>
		/// Diffuse color.
		/// </summary>
		public Color Diffuse { get; set; }

		/// <summary>
		/// Specular color.
		/// </summary>
		public Color Specular { get; set; }

		/// <summary>
		/// Coordinates for texture rendering.
		/// </summary>
		public Vector2 TexCoord { get; set; }

		/// <summary>
		/// First custom attribute
		/// </summary>
		public ushort Attributes1 { get; set; }

		/// <summary>
		/// Second custom attribute
		/// </summary>
		public ushort Attributes2 { get; set; }

		/// <summary>
		/// Third custom attribute
		/// </summary>
		public ushort Attributes3 { get; set; }


		/// <summary>
		/// Creates a new weighted corner.
		/// </summary>
		/// <param name="vertexIndex">Vertex index.</param>
		/// <param name="diffuse">Diffuse color.</param>
		/// <param name="specular">Specular color.</param>
		/// <param name="texcoord">Coordinates for texture rendering.</param>
		public WeightedCorner(ushort vertexIndex, Color diffuse, Color specular, Vector2 texcoord)
		{
			VertexIndex = vertexIndex;
			Diffuse = diffuse;
			Specular = specular;
			TexCoord = texcoord;
		}

		/// <summary>
		/// Creates a new weighted corner.
		/// </summary>
		/// <param name="vertexIndex">Vertex index.</param>
		/// <param name="diffuse">Color.</param>
		/// <param name="texcoord">Coordinates for texture rendering.</param>
		public WeightedCorner(ushort vertexIndex, Color diffuse, Vector2 texcoord)
		{
			VertexIndex = vertexIndex;
			Diffuse = diffuse;
			Specular = DefaultSpecular;
			TexCoord = texcoord;
		}

		/// <summary>
		/// Creates a new white weighted corner with default values
		/// </summary>
		/// <param name="vertexIndex">Vertex index.</param>
		public WeightedCorner(ushort vertexIndex)
		{
			VertexIndex = vertexIndex;
			Diffuse = DefaultDiffuse;
			Specular = DefaultSpecular;
			TexCoord = Vector2.Zero;
		}


		/// <inheritdoc/>
		public override readonly bool Equals(object? obj)
		{
			return obj is WeightedCorner corner &&
				   VertexIndex == corner.VertexIndex &&
				   Diffuse == corner.Diffuse &&
				   Specular == corner.Specular &&
				   TexCoord == corner.TexCoord &&
				   Attributes1 == corner.Attributes1 &&
				   Attributes2 == corner.Attributes2 &&
				   Attributes3 == corner.Attributes3;
		}

		/// <inheritdoc/>
		public override readonly int GetHashCode()
		{
			return HashCode.Combine(
				VertexIndex,
				Diffuse,
				Specular,
				TexCoord,
				Attributes1,
				Attributes2,
				Attributes3
			);
		}

		/// <inheritdoc/>
		readonly bool IEquatable<WeightedCorner>.Equals(WeightedCorner other)
		{
			return Equals(other);
		}

		/// <summary>
		/// Compares two corners for equality.
		/// </summary>
		/// <param name="l">Lefthand corners.</param>
		/// <param name="r">Righthand corners.</param>
		/// <returns>Whether the two corners are equal.</returns>
		public static bool operator ==(WeightedCorner l, WeightedCorner r)
		{
			return l.Equals(r);
		}

		/// <summary>
		/// Compares two corners for inequality.
		/// </summary>
		/// <param name="l">Lefthand corners.</param>
		/// <param name="r">Righthand corners.</param>
		/// <returns>Whether the two corners are inequal.</returns>
		public static bool operator !=(WeightedCorner l, WeightedCorner r)
		{
			return !l.Equals(r);
		}


		/// <inheritdoc/>
		public override readonly string ToString()
		{
			return $"{VertexIndex}: \t{Diffuse}, \t{Specular} \t{TexCoord.DebugString()}";
		}
	}
}
