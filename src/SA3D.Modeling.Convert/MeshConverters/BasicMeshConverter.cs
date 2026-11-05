using SA3D.Modeling.Convert.Weighted;
using SA3D.Modeling.Mesh;
using SA3D.Modeling.Mesh.Basic;
using SA3D.Modeling.Mesh.Basic.Polygon;
using SA3D.Modeling.ObjectData;
using SA3D.Modeling.ObjectData.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SA3D.Modeling.Convert.MeshConverters
{
	internal class BasicMeshConverter : MeshConverterBase
	{
		private static MeshConversionInfo? ConvertMeshToWeighted(BasicMesh mesh)
		{
			WeightedVertex[] vertices = new WeightedVertex[mesh.Positions.Length];

			for(int i = 0; i < vertices.Length; i++)
			{
				vertices[i] = new(
					mesh.Positions[i],
					mesh.Normals?[i] ?? WeightedVertex.DefaultNormal
				);
			}

			List<WeightedCorner[]> triangleSets = [];
			List<WeightedMaterial> materials = [];
			bool hasColors = false;

			for(int i = 0; i < mesh.MeshSets.Length; i++)
			{
				BasicMeshSet meshSet = mesh.MeshSets[i];
				hasColors |= meshSet.Colors != null;

				WeightedCorner[] rawCorners = new WeightedCorner[meshSet.Polygons.Sum(x => x.NumIndices)];
				int index = 0;
				foreach(IBasicPolygon polygon in meshSet.Polygons)
				{
					for(int j = 0; j < polygon.NumIndices; j++, index++)
					{
						rawCorners[index] = new(
							polygon[j],
							meshSet.Colors?[index] ?? WeightedCorner.DefaultDiffuse,
							meshSet.TextureCoordinates?[index] ?? Vector2.Zero
						);
					}
				}

				WeightedCorner[] triangles;
				switch(meshSet.PolygonType)
				{
					case BasicPolygonType.Triangles:
						triangles = rawCorners;
						break;

					case BasicPolygonType.Quads:
						triangles = [.. PolygonUtil.UnravelQuads(rawCorners)];
						break;

					case BasicPolygonType.NPoly:
					case BasicPolygonType.TriangleStrips:
						triangles = [.. PolygonUtil.UnravelStrips(rawCorners, meshSet.Polygons.Cast<BasicMultiPolygon>().Select(x => (x.NumIndices, x.Reversed)))];
						break;

					default:
						throw new InvalidOperationException($"Invalid polygon type on basic mesh set {i} of mesh {mesh.Label}!");
				}

				if(triangles.Length > 0)
				{

					triangleSets.Add(triangles);

					BasicMaterial material = mesh.Materials[meshSet.MaterialIndex];
					materials.Add(new()
					{
						Diffuse = material.DiffuseColor,
						Specular = material.SpecularColor,
						SpecularExponent = material.SpecularExponent,
						TextureIndex = material.TextureID,
						TextureFilterMode = material.Attributes.FilterMode,
						MipmapDistanceMultiplier = material.Attributes.MipmapDistanceMultiplier,
						SourceBlendMode = material.Attributes.SourceAlpha,
						DestinationBlendMode = material.Attributes.DestinationAlpha,
						AlphaMode = material.Attributes.UseAlpha ? AlphaMode.Transparent : AlphaMode.Opaque,
						DoubleSided = material.Attributes.DoubleSided,
						UseTexture = material.Attributes.UseTexture,
						AnisotropicFiltering = material.Attributes.SuperSample,
						ClampU = material.Attributes.ClampU,
						ClampV = material.Attributes.ClampV,
						MirrorU = material.Attributes.MirrorU,
						MirrorV = material.Attributes.MirrorV,
						EnvironmentMapping = material.Attributes.EnvironmentMap,
						IgnoreLighting = material.Attributes.IgnoreLighting,
						IgnoreSpecular = material.Attributes.IgnoreSpecular,
						FlatShading = material.Attributes.FlatShading
					});
				}
			}

			if(triangleSets.Count == 0)
			{
				return null;
			}

			return new(new(vertices, [.. triangleSets], [.. materials], mesh.Label))
			{
				HasNormals = mesh.Normals != null,
				HasDiffuseColors = hasColors
			};
		}

		private void ConvertWelding(MeshConversionInfo info, int meshNodeIndex)
		{
			(Node meshNode, Matrix4x4 meshNodeWorldMatrix) = NodeWorldMatrices[meshNodeIndex];
			if(meshNode.Welding == null)
			{
				return;
			}

			int[] weightNodeIndexMap = [..
				meshNode.Welding
				.SelectMany(x => x.Welds)
				.Select(x => NodeMap![x.SourceNode] - meshNodeIndex)
				.Where(x => x != 0) // safety guard
				.Distinct()
				.Order()
			];

			if(weightNodeIndexMap.Length == 0)
			{
				return;
			}

			for(int i = 0; i < info.Mesh.Vertices.Length; i++)
			{
				WeightedVertex vertex = info.Mesh.Vertices[i];
				vertex.Weights = new float[weightNodeIndexMap.Length + 1];
				vertex.Weights[0] = 1;
				info.Mesh.Vertices[i] = vertex;
			}

			info.Mesh.WeightNodeIndexMap = weightNodeIndexMap;

			int[] weightIndexMap = new int[NodeWorldMatrices.Length];
			for(int i = 0; i < weightNodeIndexMap.Length; i++)
			{
				weightIndexMap[weightNodeIndexMap[i] + meshNodeIndex] = i + 1;
			}

			if(!Matrix4x4.Invert(meshNodeWorldMatrix, out Matrix4x4 invertedMeshNodeWorldMatrix))
			{
				throw new InvalidOperationException($"Failed to invert matrix of node {meshNode.Label}");
			}

			Matrix4x4[] toLocalSpaceMatrices = [.. NodeWorldMatrices.Select(x => x.worldMatrix * invertedMeshNodeWorldMatrix)];

			foreach(VertexWelding vertexWelding in meshNode.Welding)
			{
				WeightedVertex vertex = info.Mesh.Vertices[vertexWelding.DestinationVertexIndex];
				float sum = vertexWelding.Welds.Sum(x => x.Weight);
				float weightFactor = sum > 1f ? 1f / sum : 1;
				sum = float.Min(sum, 1);

				float rootWeight = 1 - sum;
				vertex.Weights![0] = rootWeight;
				Vector3 summedPosition = rootWeight * vertex.Position;
				Vector3 summedNormal = rootWeight * vertex.Normal;

				foreach(Weld weld in vertexWelding.Welds)
				{
					int nodeIndex = NodeMap[weld.SourceNode];

					int weightIndex = weightIndexMap[nodeIndex];
					float weight = weld.Weight * weightFactor;
					vertex.Weights[weightIndex] = weight;

					BasicMesh mesh = (BasicMesh)weld.SourceNode.MeshData!;
					Matrix4x4 matrix = toLocalSpaceMatrices[nodeIndex];

					Vector3 position = Vector3.Transform(mesh.Positions[(int)weld.VertexIndex], matrix);
					Vector3 normal = mesh.Normals?[(int)weld.VertexIndex] is Vector3 weldNormal
						? Vector3.TransformNormal(weldNormal, matrix)
						: WeightedVertex.DefaultNormal;

					summedPosition += position * weight;
					summedNormal += normal * weight;
				}

				vertex.Position = summedPosition;
				vertex.Normal = Vector3.Normalize(summedNormal);

				info.Mesh.Vertices[vertexWelding.DestinationVertexIndex] = vertex;
			}
		}

		protected override MeshConversionInfo[] ConvertToWeighted()
		{
			List<MeshConversionInfo> result = [];

			for(int i = 0; i < NodeWorldMatrices.Length; i++)
			{
				Node node = NodeWorldMatrices[i].node;

				if(node.MeshData is not BasicMesh mesh)
				{
					continue;
				}

				if(ConvertMeshToWeighted(mesh) is MeshConversionInfo info)
				{
					info.AttachedNodeIndex = i;

					ConvertWelding(info, i);
					result.Add(info);
				}
			}

			return [.. result];
		}
	}
}
