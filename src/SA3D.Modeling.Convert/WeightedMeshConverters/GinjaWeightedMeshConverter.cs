using J113D.Common;
using J113D.Strippify;
using SA3D.Common.Lookup;
using SA3D.Modeling.Convert.Weighted;
using SA3D.Modeling.Mesh.Ginja;
using SA3D.Modeling.Mesh.Ginja.Enums;
using SA3D.Modeling.Mesh.Ginja.Parameters;
using SA3D.Modeling.Mesh.Ginja.Structs;
using SA3D.Modeling.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SA3D.Modeling.Convert.WeightedMeshConverters
{
	internal class GinjaWeightedMeshConverter : WeightedMeshConverterBase
	{
		private readonly List<Vector3> _positions = [];
		private readonly List<Vector3> _normals = [];
		private readonly List<Color> _colors = [];
		private readonly List<Vector2> _texcoords = [];

		private int[]? _positionsMap;
		private int[]? _normalsMap;
		private int[]? _texCoordsMap;
		private int[]? _colorsMap;


		protected override bool AllowSplitWeighted => true;

		protected override void ConvertWeighted()
		{
			for(int i = 0; i < NodeMeshes.Length; i++)
			{
				MeshConversionInfo[] meshes = NodeMeshes[i];
				if(meshes.Length == 0)
				{
					continue;
				}

				GinjaMesh ginjaMesh = ConvertToGinjaMesh(meshes, NodeWorldMatrices[i].node.Label);

				if(meshes.Any(x => !x.NoBounds))
				{
					ginjaMesh.RecalculateBounds();
				}

				NodeWorldMatrices[i].node.MeshData = ginjaMesh;
			}
		}

		private GinjaMesh ConvertToGinjaMesh(MeshConversionInfo[] meshes, string nodeLabel)
		{
			string label = meshes.Length == 1 && !string.IsNullOrWhiteSpace(meshes[0].Mesh.Label) ? meshes[0].Mesh.Label! : nodeLabel + "_mesh";

			_positions.Clear();
			_normals.Clear();
			_colors.Clear();
			_texcoords.Clear();

			List<(MeshConversionInfo, GinjaCorner[], WeightedMaterial)> meshSets = [];
			int vertexTexcoordPrecision = meshes.SelectMany(x => x.Mesh.Materials).Min(x => x.TexcoordPrecisionLevel);

			for(int i = 0; i < meshes.Length; i++)
			{
				MeshConversionInfo mesh = meshes[i];

				for(int j = 0; j < mesh.Mesh.TriangleSets.Length; j++)
				{
					WeightedCorner[] corners = mesh.Mesh.TriangleSets[j];
					WeightedMaterial material = mesh.Mesh.Materials[j];
					GinjaCorner[] ginjaCorners = new GinjaCorner[corners.Length];

					float texcoordFactor = float.Pow(2, material.TexcoordPrecisionLevel - vertexTexcoordPrecision);

					for(int c = 0; c < corners.Length; c++)
					{
						WeightedCorner corner = corners[c];

						GinjaCorner ginjaCorner = new()
						{
							PositionIndex = (ushort)(_positions.Count + corner.VertexIndex),
							NormalIndex = (ushort)(mesh.HasNormals ? _normals.Count + corner.VertexIndex : 0)
						};

						if(mesh.HasDiffuseColors)
						{
							ginjaCorner.Color0Index = (ushort)_colors.Count;
							_colors.Add(corner.Diffuse);
						}

						if(material.UseTexture)
						{
							ginjaCorner.TexCoord0Index = (ushort)_texcoords.Count;
							_texcoords.Add(corner.TexCoord * texcoordFactor);
						}

						ginjaCorners[c] = ginjaCorner;
					}

					meshSets.Add((mesh, ginjaCorners, material));
				}

				_positions.AddRange(mesh.Mesh.Vertices.Select(x => x.Position));

				if(mesh.HasNormals)
				{
					_normals.AddRange(mesh.Mesh.Vertices.Select(x => x.Normal));
				}
			}

			GinjaVertexSetArray vertexSets = ConvertVertexData(label, vertexTexcoordPrecision + 8);

			LabeledArray<GinjaMeshSet>? opaqueSets = ConvertPolygonData(meshSets, false, label);
			LabeledArray<GinjaMeshSet>? transparentSets = ConvertPolygonData(meshSets, true, label);

			return new()
			{
				Label = label,
				VertexData = vertexSets,
				OpaqueMeshes = opaqueSets,
				TransparentMeshes = transparentSets
			};
		}

		private GinjaVertexSetArray ConvertVertexData(string meshLabel, int texcoordPrecision)
		{
			float positionSSPValue = Parameters.MergeVertices ? Parameters.VertexMergeDistance : 0;
			EqualityComparer<Vector3> positionComparer = VertexUtil.CreatePosition3EqualityComparer(Parameters.MergeVertices ? Parameters.VertexMergeDistance : 0);
			(Vector3[]? finalPositions, _positionsMap) = FinalizeVertexData(_positions, VertexUtil.GetVector3SSP, positionSSPValue, positionComparer, false);

			EqualityComparer<Vector3> normalComparer = VertexUtil.CreateNormal3EqualityComparer();
			(Vector3[]? finalNormals, _normalsMap) = FinalizeVertexData(_normals, VertexUtil.GetVector3SSP, 0.001f, normalComparer, true);

			EqualityComparer<Color> colorComparer = VertexUtil.CreateByteRGBAColorQualityComparer();
			(Color[]? finalColors, _colorsMap) = FinalizeVertexData<Color, byte>(_colors, c => c.Red, 1, colorComparer, true);

			EqualityComparer<Vector2> texCoordsComparer = VertexUtil.CreatePosition2EqualityComparer(0.001f);
			(Vector2[]? finalTexCoords, _texCoordsMap) = FinalizeVertexData(_texcoords, VertexUtil.GetVector2SSP, 0.001f, texCoordsComparer, true);


			List<GinjaVertexSet> vertexSets = [];

			GinjaVertexSet positionsVertexSet = new()
			{
				Type = GinjaVertexType.Position,
				DataType = GinjaDataType.Float32,
				StructType = GinjaStructType.PositionXYZ
			};
			positionsVertexSet.SetFloatData(finalPositions, 0);
			((ILabel)positionsVertexSet.Data!).Label = meshLabel + "_vertex_positions";
			vertexSets.Add(positionsVertexSet);

			if(finalNormals != null)
			{
				GinjaVertexSet normalsVertexSet = new()
				{
					Type = GinjaVertexType.Normal,
					DataType = GinjaDataType.Float32,
					StructType = GinjaStructType.NormalXYZ
				};
				normalsVertexSet.SetFloatData(finalNormals, 0);
				((ILabel)positionsVertexSet.Data!).Label = meshLabel + "_vertex_normals";
				vertexSets.Add(normalsVertexSet);
			}

			if(finalColors != null)
			{
				vertexSets.Add(new()
				{
					Type = GinjaVertexType.Color0,
					DataType = GinjaDataType.RGBA8,
					StructType = GinjaStructType.ColorRGBA,
					ColorData = new(meshLabel + "_vertex_colors", finalColors)
				});
			}

			if(finalTexCoords != null)
			{
				GinjaVertexSet texCoordsVertexSet = new()
				{
					Type = GinjaVertexType.TexCoord0,
					DataType = GinjaDataType.Signed16,
					StructType = GinjaStructType.TexCoordUV
				};
				texCoordsVertexSet.SetFloatData(finalTexCoords, texcoordPrecision);
				((ILabel)positionsVertexSet.Data!).Label = meshLabel + "_vertex_texcoords";
				vertexSets.Add(texCoordsVertexSet);
			}

			return new(vertexSets)
			{
				Label = meshLabel + "_vertex_sets"
			};
		}

		private static (T[]? finalData, int[]? dataMap) FinalizeVertexData<T, S>(IList<T> data, Func<T, S> getSSPValue, S sspValue, EqualityComparer<T> comparer, bool allowNull) where S : INumber<S>
		{
			if(data.Count == 0)
			{
				if(!allowNull)
				{
					throw new InvalidOperationException($"Ginja mesh processing yielded no vertex positions");
				}

				return (null, null);
			}
			else if(DistinctMap<T>.TryCreateDistinctMapSSP<S>(
					data,
					getSSPValue,
					sspValue,
					comparer,
					out DistinctMap<T> distinctData
				))
			{
				return (distinctData.ValueArray, distinctData.Map);
			}
			else
			{
				return ([.. data], null);
			}
		}

		private LabeledArray<GinjaMeshSet>? ConvertPolygonData(IEnumerable<(MeshConversionInfo info, GinjaCorner[] triangleSet, WeightedMaterial material)> polygonData, bool transparent, string meshLabel)
		{
			List<GinjaMeshSet> meshSets = [];

			Dictionary<GinjaVertexType, uint> activeVertexFormats = [];
			Dictionary<GinjaParameterType, uint> activeParameters = [];

			foreach((MeshConversionInfo info, GinjaCorner[] triangleSet, WeightedMaterial material) in polygonData)
			{
				if(material.AlphaMode != Mesh.AlphaMode.Opaque != transparent)
				{
					continue;
				}

				for(int i = 0; i < triangleSet.Length;)
				{
					(triangleSet[i], triangleSet[i + 2]) = (triangleSet[i + 2], triangleSet[i]);

					for(int j = 0; j < 3; i++, j++)
					{
						GinjaCorner corner = triangleSet[i];

						// Even if a mesh does not use the given type, we still map the values to reduce 
						// the number of checks, since 0 should always map to 0 anyway

						if(_positionsMap != null)
						{
							corner.PositionIndex = (ushort)_positionsMap[corner.PositionIndex];
						}

						if(_normalsMap != null)
						{
							corner.NormalIndex = (ushort)_normalsMap[corner.NormalIndex];
						}

						if(_colorsMap != null)
						{
							corner.Color0Index = (ushort)_colorsMap[corner.Color0Index];
						}

						if(_texCoordsMap != null)
						{
							corner.TexCoord0Index = (ushort)_texCoordsMap[corner.TexCoord0Index];
						}

						triangleSet[i] = corner;
					}

				}


				List<IGinjaParameter> parameters = [];
				foreach(IGinjaParameter parameter in GetGinjaParameters(info, triangleSet, material, transparent))
				{
					if(parameter is GinjaVertexFormatParameter vertexFormatParameter)
					{
						if(!activeVertexFormats.TryGetValue(vertexFormatParameter.VertexType, out uint activeVertexFormat) || activeVertexFormat != vertexFormatParameter.Data)
						{
							activeVertexFormats[vertexFormatParameter.VertexType] = vertexFormatParameter.Data;
							parameters.Add(parameter);
						}
					}
					else if(!activeParameters.TryGetValue(parameter.Type, out uint activeParameter) || activeParameter != parameter.Data)
					{
						activeParameters[parameter.Type] = parameter.Data;
						parameters.Add(parameter);
					}
				}

				List<GinjaPolygon> polygons = [];
				List<GinjaCorner> triangles = [];

				foreach(GinjaCorner[] strip in TriangleStrippifier.Global.Strippify(triangleSet, c => c.PositionIndex, 1))
				{
					if(strip.Length == 3)
					{
						triangles.AddRange(strip);
						continue;
					}

					polygons.Add(new(GinjaPolyType.TriangleStrip, strip));
				}

				if(triangles.Count > 0)
				{
					polygons.Add(new(GinjaPolyType.Triangles, [.. triangles]));
				}

				if(parameters.Count > 0 || polygons.Count > 0)
				{
					meshSets.Add(new()
					{
						Parameters = parameters.Count == 0 ? null : new($"{meshLabel}_mesh_parameters_{meshSets.Count}", [.. parameters]),
						Polygons = polygons.Count == 0 ? null : new([.. polygons]) { Label = $"{meshLabel}_mesh_polygons_{meshSets.Count}" }
					});
				}
			}

			if(meshSets.Count == 0)
			{
				return null;
			}

			return new([.. meshSets]);
		}

		private static IEnumerable<IGinjaParameter> GetGinjaParameters(MeshConversionInfo info, GinjaCorner[] triangleSet, WeightedMaterial material, bool transparent)
		{
			GinjaIndexFormat indexFormat = GinjaIndexFormat.HasPosition;
			if(triangleSet.Any(x => x.PositionIndex > byte.MaxValue))
			{
				indexFormat |= GinjaIndexFormat.PositionLargeIndex;
			}

			yield return new GinjaVertexFormatParameter()
			{
				VertexType = GinjaVertexType.Position,
				VertexStructType = GinjaStructType.PositionXYZ,
				VertexDataType = GinjaDataType.Float32
			};

			if(info.HasNormals)
			{
				indexFormat |= GinjaIndexFormat.HasNormal;

				if(triangleSet.Any(x => x.NormalIndex > byte.MaxValue))
				{
					indexFormat |= GinjaIndexFormat.NormalLargeIndex;
				}

				yield return new GinjaVertexFormatParameter()
				{
					VertexType = GinjaVertexType.Normal,
					VertexStructType = GinjaStructType.NormalXYZ,
					VertexDataType = GinjaDataType.Float32
				};
			}

			if(info.HasDiffuseColors)
			{
				indexFormat |= GinjaIndexFormat.HasColor0;

				if(triangleSet.Any(x => x.Color0Index > byte.MaxValue))
				{
					indexFormat |= GinjaIndexFormat.Color0LargeIndex;
				}

				yield return new GinjaVertexFormatParameter()
				{
					VertexType = GinjaVertexType.Color0,
					VertexStructType = GinjaStructType.ColorRGBA,
					VertexDataType = GinjaDataType.RGBA8
				};
			}

			if(material.UseTexture)
			{
				indexFormat |= GinjaIndexFormat.HasTexCoord0;

				if(triangleSet.Any(x => x.TexCoord0Index > byte.MaxValue))
				{
					indexFormat |= GinjaIndexFormat.TexCoord0LargeIndex;
				}

				yield return new GinjaVertexFormatParameter()
				{
					VertexType = GinjaVertexType.TexCoord0,
					VertexStructType = GinjaStructType.TexCoordUV,
					VertexDataType = GinjaDataType.Signed16,
					FractionalBitCount = (byte)(8 + material.TexcoordPrecisionLevel)
				};
			}

			yield return new GinjaIndexFormatParameter()
			{
				IndexFormat = indexFormat
			};

			yield return new GinjaStripFlagsParameter()
			{
				ChannelCount = material.GinjaChannelCount,
				TexGenCount = material.GinjaTexGenCount,
				IgnoreLight = material.NoLighting,
				IgnoreSpecular = material.NoSpecular,
				IgnoreAmbient = material.NoAmbient,
				UseVertexColorForDiffuse = material.GinjaUseVertexColorForDiffuse,
				UseVertexColorForAmbient = material.GinjaUseVertexColorForAmbient,
				UseAlpha = transparent,
				NoPunchThrough = transparent && material.AlphaMode == Mesh.AlphaMode.TransparentForceAlphaClipOff,
				DoubleSided = !material.BackfaceCulling,
				TevStageCount = material.GinjaTevStageCount
			};

			yield return new GinjaBlendAlphaParameter()
			{
				SourceAlpha = material.SourceBlendMode,
				DestinationAlpha = material.DestinationBlendmode,
				UseAlpha = transparent
			};

			yield return new GinjaDiffuseColorParameter()
			{
				DiffuseColor = material.Diffuse
			};

			if(!material.NoAmbient)
			{
				yield return new GinjaAmbientColorParameter()
				{
					AmbientColor = material.Ambient
				};
			}

			if(!material.NoSpecular)
			{
				yield return new GinjaSpecularColorParameter()
				{
					SpecularColor = material.Specular
				};
			}

			yield return new GinjaTextureParameter()
			{
				TextureID = (ushort)material.TextureIndex,
				Tiling = (material.MirrorU ? GinjaTileMode.MirrorU : 0)
							| (material.MirrorV ? GinjaTileMode.MirrorV : 0)
							| (material.ClampU ? 0 : GinjaTileMode.RepeatU)
							| (material.ClampV ? 0 : GinjaTileMode.RepeatV)
			};

			yield return material.GinjaTevStageParameter;
			yield return material.GinjaTexGenParameter;
		}
	}
}
