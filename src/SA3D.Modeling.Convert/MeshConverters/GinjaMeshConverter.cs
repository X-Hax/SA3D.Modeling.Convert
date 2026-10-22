using SA3D.Common.Lookup;
using SA3D.Modeling.Convert.Weighted;
using SA3D.Modeling.Mesh;
using SA3D.Modeling.Mesh.Ginja;
using SA3D.Modeling.Mesh.Ginja.Enums;
using SA3D.Modeling.Mesh.Ginja.Parameters;
using SA3D.Modeling.Mesh.Ginja.Structs;
using SA3D.Modeling.ObjectData;
using SA3D.Modeling.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SA3D.Modeling.Convert.MeshConverters
{
	internal class GinjaMeshConverter : MeshConverterBase
	{
		private class MeshSetConversionContext
		{
			public GinjaVertexSetArray VertexSets { get; }

			public List<WeightedVertex> Vertices { get; } = [];

			public Dictionary<uint, ushort> VertexMapping { get; } = [];

			public List<WeightedCorner[]> TriangleSets { get; } = [];

			public List<WeightedMaterial> Materials { get; } = [];

			public bool HasNormals { get; set; }

			public bool HasColors { get; set; }

			public MeshSetConversionContext(GinjaVertexSetArray vertexSets)
			{
				VertexSets = vertexSets;
			}
		}

		private static void ConvertGinjaMeshset(string meshName, MeshSetConversionContext context, IEnumerable<GinjaMeshSet> meshSets)
		{
			Vector3[]? positions = null;
			Vector3[]? normals = null;
			Vector2[]? texcoords = null;
			Color[]? colors = null;

			GinjaIndexFormat indexFormat = default;

			bool CheckHasData(GinjaIndexFormat format, object? data)
			{
				bool result = indexFormat.HasFlag(format);
				if(result && data == null)
				{
					throw new InvalidOperationException($"Index format on mesh {meshName} declares usage of {format.ToString()[3..]} data, but declares no corresponding vertex data!");
				}

				return result;
			}

			WeightedMaterial material = WeightedMaterial.DefaultValues;

			foreach(GinjaMeshSet meshSet in meshSets)
			{
				if(meshSet.Parameters != null)
				{
					foreach(IGinjaParameter parameter in meshSet.Parameters)
					{
						switch(parameter)
						{
							case GinjaVertexFormatParameter vertexFormatParameter:

								GinjaVertexSet? vertexSet = context.VertexSets.FirstOrDefault(x => x.Type == vertexFormatParameter.VertexType)
									?? throw new InvalidOperationException($"Vertex parameter on mesh \"{meshName}\" uses type \"{vertexFormatParameter.VertexType}\" without having a corresponding vertex set");

								if(vertexSet.DataType != vertexFormatParameter.VertexDataType
									|| vertexSet.StructType != vertexFormatParameter.VertexStructType)
								{
									throw new InvalidOperationException($"The \"{vertexFormatParameter.VertexType}\" vertex parameter and set on mesh \"{meshName}\" have mismatching data/struct types");
								}

								switch(vertexFormatParameter.VertexType)
								{
									case GinjaVertexType.Position:
										positions = vertexSet.GetDataAsVector3(vertexFormatParameter.FractionalBitCount);
										break;
									case GinjaVertexType.Normal:
										normals = vertexSet.GetDataAsVector3(vertexFormatParameter.FractionalBitCount);
										break;
									case GinjaVertexType.Color0:
										colors = ((LabeledArray<Color>)vertexSet.Data!).Array;
										break;
									case GinjaVertexType.TexCoord0:
										material.TexcoordPrecisionLevel = vertexFormatParameter.FractionalBitCount;
										texcoords = vertexSet.GetDataAsVector2(vertexFormatParameter.FractionalBitCount);
										break;
									default:
										break;
								}

								break;

							case GinjaIndexFormatParameter indexFormatParameter:
								indexFormat = indexFormatParameter.IndexFormat;
								material.UseTexture = indexFormat.HasFlag(GinjaIndexFormat.HasTexCoord0);
								break;

							case GinjaStripFlagsParameter stripFlagsParameter:
								material.NoLighting = stripFlagsParameter.IgnoreLight;
								material.NoSpecular = stripFlagsParameter.IgnoreSpecular;
								material.NoAmbient = stripFlagsParameter.IgnoreAmbient;

								if(!stripFlagsParameter.UseAlpha)
								{
									material.AlphaMode = AlphaMode.Opaque;
								}
								else if(stripFlagsParameter.NoPunchThrough)
								{
									material.AlphaMode = AlphaMode.TransparentForceAlphaClipOff;
								}
								else
								{
									material.AlphaMode = AlphaMode.TransparentForceAlphaClipOn;
								}

								material.BackfaceCulling = !stripFlagsParameter.DoubleSided;

								material.GinjaChannelCount = stripFlagsParameter.ChannelCount;
								material.GinjaTexGenCount = stripFlagsParameter.TexGenCount;
								material.GinjaTevStageCount = stripFlagsParameter.TevStageCount;

								material.GinjaUseVertexColorForDiffuse = stripFlagsParameter.UseVertexColorForDiffuse;
								material.GinjaUseVertexColorForAmbient = stripFlagsParameter.UseVertexColorForAmbient;
								break;

							case GinjaBlendAlphaParameter blendAlphaParameter:
								material.SourceBlendMode = blendAlphaParameter.SourceAlpha;
								material.DestinationBlendmode = blendAlphaParameter.DestinationAlpha;
								// material.AlphaMode // we ignore this and instead rely on the flags in GinjaStripFlagsParameter
								break;

							case GinjaDiffuseColorParameter diffuseColorParameter:
								material.Diffuse = diffuseColorParameter.DiffuseColor;
								break;

							case GinjaAmbientColorParameter ambientColorParameter:
								material.Ambient = ambientColorParameter.AmbientColor;
								break;

							case GinjaSpecularColorParameter specularColorParameter:
								material.Specular = specularColorParameter.SpecularColor;
								break;

							case GinjaTextureParameter textureParameter:
								material.TextureIndex = textureParameter.TextureID;
								material.GinjaTextureSlot = textureParameter.TextureSlot;
								material.ClampU = !textureParameter.Tiling.HasFlag(GinjaTileMode.RepeatU);
								material.ClampV = !textureParameter.Tiling.HasFlag(GinjaTileMode.RepeatV);
								material.MirrorU = textureParameter.Tiling.HasFlag(GinjaTileMode.MirrorU);
								material.MirrorV = textureParameter.Tiling.HasFlag(GinjaTileMode.MirrorV);
								break;

							case GinjaTevStageParameter tevStageParameter:
								material.GinjaTevStageParameter = tevStageParameter;
								break;

							case GinjaTexGenParameter texGenParameter:
								material.GinjaTexGenParameter = texGenParameter;
								break;
						}
					}
				}

				if(meshSet.Polygons != null)
				{
					if(!CheckHasData(GinjaIndexFormat.HasPosition, positions))
					{
						throw new InvalidOperationException($"A meshset on mesh \"{meshName}\" has no positions");
					}

					bool hasNormals = CheckHasData(GinjaIndexFormat.HasNormal, normals);
					context.HasNormals |= hasNormals;

					bool hasColors = CheckHasData(GinjaIndexFormat.HasColor0, colors);
					context.HasColors |= hasColors;

					bool hasTexcoords = CheckHasData(GinjaIndexFormat.HasTexCoord0, texcoords);


					List<WeightedCorner> corners = [];

					foreach(GinjaPolygon polygon in meshSet.Polygons)
					{
						WeightedCorner[] rawCorners = new WeightedCorner[polygon.Corners.Length];
						for(int i = 0; i < rawCorners.Length; i++)
						{
							GinjaCorner ginjaCorner = polygon.Corners[i];

							uint vertexIndex = ginjaCorner.PositionIndex;
							Vector3 position = positions![ginjaCorner.PositionIndex];
							Vector3 normal = WeightedVertex.DefaultNormal;

							if(hasNormals)
							{
								vertexIndex |= (uint)(ginjaCorner.NormalIndex << 16);
								normal = normals![ginjaCorner.NormalIndex];
							}

							if(!context.VertexMapping.TryGetValue(vertexIndex, out ushort mappedVertexIndex))
							{
								mappedVertexIndex = (ushort)context.Vertices.Count;
								context.Vertices.Add(new(position, normal));
								context.VertexMapping[vertexIndex] = mappedVertexIndex;
							}

							rawCorners[i] = new(
								mappedVertexIndex,
								hasColors ? colors![ginjaCorner.Color0Index] : WeightedCorner.DefaultDiffuse,
								hasTexcoords ? texcoords![ginjaCorner.TexCoord0Index] : default
							);
						}

						switch(polygon.Type)
						{
							case GinjaPolyType.Triangles:
								corners.AddRange(rawCorners);
								break;

							case GinjaPolyType.TriangleStrip:
								corners.AddRange(PolygonUtil.UnravelStrip(rawCorners, false));
								break;

							default:
								throw new InvalidOperationException($"Invalid polygon type {polygon.Type} on Mesh \"{meshName}\"");
						}
					}

					if(corners.Count > 0)
					{
						WeightedCorner[] cornerResult = [.. corners];

						// Ginja uses a flipped triangle corner order, for some reason
						for(int i = 0; i < cornerResult.Length; i += 3)
						{
							(cornerResult[i], cornerResult[i + 2]) = (cornerResult[i + 2], cornerResult[i]);
						}

						context.TriangleSets.Add(cornerResult);
						context.Materials.Add(material);
					}
				}
			}
		}

		private static MeshConversionInfo? ConvertMeshToWeighted(GinjaMesh mesh)
		{
			if(mesh.VertexData == null)
			{
				throw new InvalidOperationException($"Mesh \"{mesh.Label}\" has no vertex sets");
			}

			MeshSetConversionContext context = new(mesh.VertexData);

			if(mesh.OpaqueMeshes != null)
			{
				ConvertGinjaMeshset(mesh.Label, context, mesh.OpaqueMeshes);
			}

			if(mesh.TransparentMeshes != null)
			{
				ConvertGinjaMeshset(mesh.Label, context, mesh.TransparentMeshes);
			}

			if(context.TriangleSets.Count == 0)
			{
				return null;
			}

			return new(new([.. context.Vertices], [.. context.TriangleSets], [.. context.Materials], mesh.Label))
			{
				HasDiffuseColors = context.HasColors,
				HasNormals = context.HasNormals
			};
		}

		protected override MeshConversionInfo[] ConvertToWeighted()
		{
			List<MeshConversionInfo> result = [];
			Dictionary<GinjaMesh, MeshConversionInfo?> converted = [];

			for(int i = 0; i < NodeWorldMatrices.Length; i++)
			{
				Node node = NodeWorldMatrices[i].node;

				if(node.MeshData is not GinjaMesh mesh)
				{
					continue;
				}

				if(!converted.TryGetValue(mesh, out MeshConversionInfo? info))
				{
					info = ConvertMeshToWeighted(mesh);
					converted.Add(mesh, info);

					if(info != null)
					{
						result.Add(info);
					}
				}

				if(info != null)
				{
					_ = info.AttachedNodeIndices.Add(i);
				}
			}

			return [.. result];
		}
	}
}
