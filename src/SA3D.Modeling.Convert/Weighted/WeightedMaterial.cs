using SA3D.Modeling.Mesh;
using SA3D.Modeling.Mesh.Ginja.Parameters;
using SA3D.Modeling.Structs;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.Marshalling;

namespace SA3D.Modeling.Convert.Weighted
{
	/// <summary>
	/// Material (surface rendering) properties for a weighted mesh.
	/// </summary>
	public struct WeightedMaterial : IEquatable<WeightedMaterial>
	{
		/// <summary>
		/// Default material values.
		/// </summary>
		public static readonly WeightedMaterial DefaultValues = new()
		{
			Diffuse = Color.ColorWhite,
			Specular = Color.ColorWhite,
			SpecularExponent = 11,
			Ambient = Color.ColorWhite,
			MipmapDistanceMultiplier = 1,
			SourceBlendMode = BlendMode.SrcAlpha,
			DestinationBlendMode = BlendMode.SrcAlphaInverted,
			TextureFilterMode = FilterMode.Bilinear,
			UseTexture = true,

			GinjaTevStageParameter = GinjaTevStageParameter.DefaultValues,
			GinjaTexGenParameter = GinjaTexGenParameter.DefaultValues,
			GinjaUseVertexColorForDiffuse = true
		};

		#region Storage properties

		/// <summary>
		/// The diffuse color.
		/// </summary>
		public Color Diffuse { readonly get; set; }

		/// <summary>
		/// The specular color.
		/// </summary>
		public Color Specular { readonly get; set; }

		/// <summary>
		/// The specular exponent.
		/// </summary>
		public float SpecularExponent { readonly get; set; }

		/// <summary>
		/// The Ambient color.
		/// </summary>
		public Color Ambient { readonly get; set; }

		/// <summary>
		/// Texture Index.
		/// </summary>
		public uint TextureIndex { readonly get; set; }

		/// <summary>
		/// Texture filtering mode.
		/// </summary>
		public FilterMode TextureFilterMode { readonly get; set; }

		/// <summary>
		/// Mipmap distance multiplier.
		/// </summary>
		public float MipmapDistanceMultiplier { readonly get; set; }

		/// <summary>
		/// Source blend mode.
		/// </summary>
		public BlendMode SourceBlendMode { readonly get; set; }

		/// <summary>
		/// Destination blend mode.
		/// </summary>
		public BlendMode DestinationBlendMode { readonly get; set; }

		/// <summary>
		/// Additional Material attributes.
		/// </summary>
		public WeightedMaterialAttributes Attributes { readonly get; set; }

		/// <summary>
		/// Texture coordinate precision level. Every level higher allows for double the previous precision, but also reduces range by half.
		/// <br/> Supported:
		/// <br/> - Basic: 0
		/// <br/> - Chunk: 0 or 2
		/// <br/> - Gamecube: 0-7
		/// <br/>
		/// <br/> When converting, the lowest supported precision is used.
		/// </summary>
		public byte TexcoordPrecisionLevel { get; set; }

		/// <summary>
		/// Number of polygon attributes (ranges from 0 to 3)
		/// </summary>
		public byte PolygonAttributeCount
		{
			get;
			set
			{
				if(value is < 0 or > 3)
				{
					throw new ArgumentOutOfRangeException(nameof(value), "Value out of range. Must be between 0 and 3.");
				}

				field = value;
			}
		}

		#endregion

		#region Attribute Properties

		/// <summary>
		/// Disable alpha testing
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public AlphaMode AlphaMode
		{
			readonly get => (AlphaMode)(Attributes & WeightedMaterialAttributes.AlphaMode);
			set => Attributes = (Attributes & ~WeightedMaterialAttributes.AlphaMode) | (WeightedMaterialAttributes)value;
		}

		/// <summary>
		/// Disables backface culling.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool DoubleSided
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.DoubleSided);
			set => SetAttributes(WeightedMaterialAttributes.DoubleSided, value);
		}

		/// <summary>
		/// Whether textures should be rendered.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool UseTexture
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.UseTexture);
			set => SetAttributes(WeightedMaterialAttributes.UseTexture, value);
		}

		/// <summary>
		/// Enables anisotropic filtering.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool AnisotropicFiltering
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.AnisotropicFiltering);
			set => SetAttributes(WeightedMaterialAttributes.AnisotropicFiltering, value);
		}

		/// <summary>
		/// Clamps texture corrdinates along the horizontal axis between -1 and 1.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool ClampU
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.ClampU);
			set => SetAttributes(WeightedMaterialAttributes.ClampU, value);
		}

		/// <summary>
		/// Clamps texture corrdinates along the vertical axis between -1 and 1.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool ClampV
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.ClampV);
			set => SetAttributes(WeightedMaterialAttributes.ClampV, value);
		}

		/// <summary>
		/// Mirrors texture coordinates along the horizontal axis every other full unit.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool MirrorU
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.MirrorU);
			set => SetAttributes(WeightedMaterialAttributes.MirrorU, value);
		}

		/// <summary>
		/// Mirrors texture coordinates along the vertical axis every other full unit.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool MirrorV
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.MirrorV);
			set => SetAttributes(WeightedMaterialAttributes.MirrorV, value);
		}

		/// <summary>
		/// Whether to use normal mapping for textures.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool EnvironmentMapping
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.NormalMapping);
			set => SetAttributes(WeightedMaterialAttributes.NormalMapping, value);
		}

		/// <summary>
		/// Ignores lighting as a whole.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool IgnoreLighting
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.NoLighting);
			set => SetAttributes(WeightedMaterialAttributes.NoLighting, value);
		}

		/// <summary>
		/// Ignores ambient lighting.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool IgnoreAmbient
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.NoAmbient);
			set => SetAttributes(WeightedMaterialAttributes.NoAmbient, value);
		}

		/// <summary>
		/// Ignores specular lighting.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool IgnoreSpecular
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.NoSpecular);
			set => SetAttributes(WeightedMaterialAttributes.NoSpecular, value);
		}

		/// <summary>
		/// Ignores interpolated normals and instead renders every polygon flat.
		/// <br/> Wrapper around flag in <see cref="Attributes"/>.
		/// </summary>
		public bool FlatShading
		{
			readonly get => HasAttributes(WeightedMaterialAttributes.Flat);
			set => SetAttributes(WeightedMaterialAttributes.Flat, value);
		}

		#endregion

		#region Gamecube Properties

		/// <summary>
		/// Use vertex colors for diffuse color, instead of <see cref="Diffuse"/>
		/// </summary>
		public bool GinjaUseVertexColorForDiffuse { readonly get; set; }

		/// <summary>
		/// Use vertex colors for ambient color, instead of <see cref="Ambient"/>
		/// </summary>
		public bool GinjaUseVertexColorForAmbient { readonly get; set; }

		/// <summary>
		/// Ginja texture slot to use
		/// </summary>
		public byte GinjaTextureSlot { readonly get; set; }

		/// <summary>
		/// Ginja tev stage parameter container
		/// </summary>
		public GinjaTevStageParameter GinjaTevStageParameter { readonly get; set; }

		/// <summary>
		/// Ginja tex gen parameter container
		/// </summary>
		public GinjaTexGenParameter GinjaTexGenParameter { readonly get; set; }

		#endregion

		/// <summary>
		/// Empty default constructor.
		/// </summary>
		public WeightedMaterial() : this(DefaultValues) { }

		/// <summary>
		/// Creates a new weighted material from a template.
		/// </summary>
		/// <param name="template">The template to use.</param>
		public WeightedMaterial(WeightedMaterial template)
		{
			Diffuse = template.Diffuse;
			Specular = template.Specular;
			SpecularExponent = template.SpecularExponent;
			Ambient = template.Ambient;
			TextureIndex = template.TextureIndex;
			TextureFilterMode = template.TextureFilterMode;
			MipmapDistanceMultiplier = template.MipmapDistanceMultiplier;
			SourceBlendMode = template.SourceBlendMode;
			DestinationBlendMode = template.DestinationBlendMode;
			Attributes = template.Attributes;

			GinjaTevStageParameter = template.GinjaTevStageParameter;
			GinjaTexGenParameter = template.GinjaTexGenParameter;
			GinjaUseVertexColorForDiffuse = template.GinjaUseVertexColorForDiffuse;
			GinjaUseVertexColorForAmbient = template.GinjaUseVertexColorForAmbient;
			GinjaTextureSlot = template.GinjaTextureSlot;
		}


		/// <summary>
		/// Set material attributes.
		/// </summary>
		/// <param name="attrib">The attributes to set.</param>
		/// <param name="state">New state for the attributes.</param>
		public void SetAttributes(WeightedMaterialAttributes attrib, bool state)
		{
			if(state)
			{
				Attributes |= attrib;
			}
			else
			{
				Attributes &= ~attrib;
			}
		}

		/// <summary>
		/// Checks if materials attributes are set.
		/// </summary>
		/// <param name="attrib">The attributes to check.</param>
		/// <returns>Whether all specified attributes are set.</returns>
		public readonly bool HasAttributes(WeightedMaterialAttributes attrib)
		{
			return Attributes.HasFlag(attrib);
		}


		readonly bool IEquatable<WeightedMaterial>.Equals(WeightedMaterial other)
		{
			return Equals(other);
		}

		/// <inheritdoc/>
		public override readonly bool Equals(object? obj)
		{
			return obj is WeightedMaterial material &&
				   Diffuse.Equals(material.Diffuse) &&
				   Specular.Equals(material.Specular) &&
				   SpecularExponent == material.SpecularExponent &&
				   Ambient.Equals(material.Ambient) &&
				   TextureIndex == material.TextureIndex &&
				   TextureFilterMode == material.TextureFilterMode &&
				   MipmapDistanceMultiplier == material.MipmapDistanceMultiplier &&
				   SourceBlendMode == material.SourceBlendMode &&
				   DestinationBlendMode == material.DestinationBlendMode &&
				   Attributes == material.Attributes &&
				   TexcoordPrecisionLevel == material.TexcoordPrecisionLevel &&
				   PolygonAttributeCount == material.PolygonAttributeCount &&
				   GinjaUseVertexColorForDiffuse == material.GinjaUseVertexColorForDiffuse &&
				   GinjaUseVertexColorForAmbient == material.GinjaUseVertexColorForAmbient &&
				   GinjaTextureSlot == material.GinjaTextureSlot &&
				   GinjaTevStageParameter.Data == material.GinjaTevStageParameter.Data &&
				   GinjaTexGenParameter.Data == material.GinjaTexGenParameter.Data;
		}

		/// <inheritdoc/>
		public override readonly int GetHashCode()
		{
			HashCode hash = new();
			hash.Add(Diffuse);
			hash.Add(Specular);
			hash.Add(SpecularExponent);
			hash.Add(Ambient);
			hash.Add(TextureIndex);
			hash.Add(TextureFilterMode);
			hash.Add(MipmapDistanceMultiplier);
			hash.Add(SourceBlendMode);
			hash.Add(DestinationBlendMode);
			hash.Add(Attributes);
			hash.Add(TexcoordPrecisionLevel);
			hash.Add(PolygonAttributeCount);
			hash.Add(AlphaMode);
			hash.Add(DoubleSided);
			hash.Add(UseTexture);
			hash.Add(AnisotropicFiltering);
			hash.Add(ClampU);
			hash.Add(ClampV);
			hash.Add(MirrorU);
			hash.Add(MirrorV);
			hash.Add(EnvironmentMapping);
			hash.Add(IgnoreLighting);
			hash.Add(IgnoreAmbient);
			hash.Add(IgnoreSpecular);
			hash.Add(FlatShading);
			hash.Add(GinjaUseVertexColorForDiffuse);
			hash.Add(GinjaUseVertexColorForAmbient);
			hash.Add(GinjaTextureSlot);
			hash.Add(GinjaTevStageParameter);
			hash.Add(GinjaTexGenParameter);
			return hash.ToHashCode();
		}



		/// <summary>
		/// Compares two materials for equality.
		/// </summary>
		/// <param name="l">Lefthand materials.</param>
		/// <param name="r">Righthand materials.</param>
		/// <returns>Whether the two materials are equal.</returns>
		public static bool operator ==(WeightedMaterial l, WeightedMaterial r)
		{
			return l.Equals(r);
		}

		/// <summary>
		/// Compares two materials for inequality.
		/// </summary>
		/// <param name="l">Lefthand materials.</param>
		/// <param name="r">Righthand materials.</param>
		/// <returns>Whether the two materials are inequal.</returns>
		public static bool operator !=(WeightedMaterial l, WeightedMaterial r)
		{
			return !l.Equals(r);
		}
	}
}
