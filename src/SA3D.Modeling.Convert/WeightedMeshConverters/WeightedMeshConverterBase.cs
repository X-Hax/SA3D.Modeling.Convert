using SA3D.Common;
using SA3D.Modeling.Convert.Weighted;
using SA3D.Modeling.ObjectData;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

namespace SA3D.Modeling.Convert.WeightedMeshConverters
{
	internal abstract partial class WeightedMeshConverterBase
	{
		protected Node RootNode { get; private set; }
		protected (Node node, Matrix4x4 worldMatrix)[] NodeWorldMatrices { get; private set; }
		protected Dictionary<Node, int> NodeMap { get; private set; }

		protected MeshConversionInfo[] Meshes { get; private set; }
		protected MeshConversionInfo[][] NodeMeshes { get; private set; }

		protected WeightedMeshConverterParameters Parameters { get; private set; }

		protected abstract bool AllowSplitWeighted { get; }


		protected WeightedMeshConverterBase()
		{
			RootNode = new();
			NodeWorldMatrices = [];
			NodeMap = [];
			Meshes = [];
			NodeMeshes = [];
		}


		protected Matrix4x4 GetNodeSpaceTransform(int sourceNode, int destinationNode)
		{
			Matrix4x4 attachedNodeMatrix = NodeWorldMatrices[sourceNode].worldMatrix;

			Matrix4x4 targetNodeMatrix = NodeWorldMatrices[destinationNode].worldMatrix;
			Matrix4x4.Invert(targetNodeMatrix, out Matrix4x4 inverseTargetNodeMatrix);

			return attachedNodeMatrix * inverseTargetNodeMatrix;
		}

		public void ConvertWeighted(Node targetModel, IEnumerable<MeshConversionInfo> meshes, WeightedMeshConverterParameters parameters)
		{
			if(targetModel.Any(x => x.MeshData != null))
			{
				throw new InvalidOperationException("The model you are trying to convert into already has mesh data!");
			}

			RootNode = targetModel;
			NodeWorldMatrices = targetModel.GetWorldMatrixTree();
			NodeMap = targetModel.GetTreeNodeEnumerable().Select((x, i) => (x, i)).ToDictionary();

			Parameters = parameters;


			Meshes = AllowSplitWeighted && parameters.SplitWeighted
				? [.. meshes.SelectMany(SplitMesh)]
				: [.. meshes];

			NodeMeshes = new MeshConversionInfo[NodeWorldMatrices.Length][];
			for(int i = 0; i < NodeMeshes.Length; i++)
			{
				NodeMeshes[i] = [.. Meshes.Where(x => x.AttachedNodeIndex == i)];
			}

			ConvertWeighted();
		}

		protected abstract void ConvertWeighted();


		#region Mesh splitter code

		private MeshConversionInfo[] SplitMesh(MeshConversionInfo mesh)
		{
			if(mesh.Mesh.WeightNodeIndexMap == null)
			{
				return [mesh];
			}

			WeightedCorner[][][] splitTriangleSets = AssignTrianglesToSplits(mesh);

			List<MeshConversionInfo> result = [];

			for(int i = 0; i < splitTriangleSets.Length; i++)
			{
				if(MeshFromSplitTriangles(mesh, i, splitTriangleSets[i]) is MeshConversionInfo info)
				{
					result.Add(info);
				}
			}

			return [.. result];
		}

		private static WeightedCorner[][][] AssignTrianglesToSplits(MeshConversionInfo mesh)
		{
			WeightedCorner[][][] result = new WeightedCorner[mesh.Mesh.WeightNodeIndexMap!.Length + 1][][];
			for(int i = 0; i < result.Length; i++)
			{
				result[i] = new WeightedCorner[mesh.Mesh.TriangleSets.Length][];
			}

			WeightedCorner[] triangle = new WeightedCorner[3];

			foreach((int triangleSetIndex, WeightedCorner[] triangleSet) in mesh.Mesh.TriangleSets.Index())
			{
				List<WeightedCorner>[] splitTriangleSets = new List<WeightedCorner>[result.Length];
				for(int i = 0; i < result.Length; i++)
				{
					splitTriangleSets[i] = [];
				}

				for(int j = 0; j < triangleSet.Length;)
				{
					float maxWeight = 0;
					int maxIndex = 0;

					for(int v = 0; v < 3; v++, j++)
					{
						WeightedCorner corner = triangleSet[j];
						triangle[v] = corner;

						ushort vertexIndex = corner.VertexIndex;
						WeightedVertex vertex = mesh.Mesh.Vertices[vertexIndex];

						for(int w = 0; w < vertex.Weights!.Length; w++)
						{
							float weight = vertex.Weights[w];
							// to ensure no "jagged" seams, we prefer earlier nodes
							if(weight > maxWeight || (weight == maxWeight && w < maxIndex))
							{
								maxWeight = weight;
								maxIndex = w;
							}
						}
					}

					splitTriangleSets[maxIndex].AddRange(triangle);
				}

				foreach((int splitIndex, List<WeightedCorner> splitTriangleSet) in splitTriangleSets.Index())
				{
					result[splitIndex][triangleSetIndex] = [.. splitTriangleSet];
				}
			}

			return result;
		}

		private MeshConversionInfo? MeshFromSplitTriangles(MeshConversionInfo mesh, int splitIndex, WeightedCorner[][] splitTriangleSets)
		{
			if(!FinalizeSplitMeshData(mesh, splitIndex, splitTriangleSets,
				out WeightedVertex[]? vertices,
				out WeightedCorner[][]? triangleSets,
				out WeightedMaterial[]? materials,
				out SortedSet<int>? usedWeightIndices))
			{
				return null;
			}

			int relativeNodeIndex = splitIndex > 0 ? mesh.Mesh.WeightNodeIndexMap![splitIndex - 1] : 0;

			// We pass the polygon data (and weight arrays) over directly, instead of copying it,
			// since these splits should never make it out of the converter process.

			MeshConversionInfo splitMesh = new(new(vertices.ContentClone(), triangleSets, materials))
			{
				HasNormals = mesh.HasNormals,
				HasDiffuseColors = mesh.HasDiffuseColors,
				NoBounds = mesh.NoBounds,
				VolumeMesh = mesh.VolumeMesh,
				ChunkVertexType = mesh.ChunkVertexType
			};

			splitMesh.Mesh.Label = $"{mesh.Mesh.Label}_Split_{splitIndex}";
			splitMesh.AttachedNodeIndex = mesh.AttachedNodeIndex + relativeNodeIndex;

			int weightMapSize = usedWeightIndices.Count(x => x != splitIndex);
			if(weightMapSize > 0)
			{
				splitMesh.Mesh.WeightNodeIndexMap = new int[weightMapSize];

				foreach((int j, int usedWeightIndex) in usedWeightIndices.Where(x => x != splitIndex).Index())
				{
					int weightNodeIndex = usedWeightIndex == 0 ? 0 : mesh.Mesh.WeightNodeIndexMap![usedWeightIndex - 1];
					splitMesh.Mesh.WeightNodeIndexMap[j] = weightNodeIndex - relativeNodeIndex;
				}
			}

			if(splitIndex > 0)
			{
				Matrix4x4 toLocalSpaceMatrix = GetNodeSpaceTransform(mesh.AttachedNodeIndex, splitMesh.AttachedNodeIndex);

				for(int v = 0; v < splitMesh.Mesh.Vertices.Length; v++)
				{
					WeightedVertex vertex = splitMesh.Mesh.Vertices[v];
					vertex.Position = Vector3.Transform(vertex.Position, toLocalSpaceMatrix);

					if(splitMesh.HasNormals)
					{
						vertex.Normal = Vector3.TransformNormal(vertex.Normal, toLocalSpaceMatrix);
					}

					splitMesh.Mesh.Vertices[v] = vertex;
				}
			}

			return splitMesh;
		}

		private static bool FinalizeSplitMeshData(
			MeshConversionInfo mesh,
			int splitIndex,
			WeightedCorner[][] splitTriangleSets,
			[NotNullWhen(true)] out WeightedVertex[]? vertices,
			[NotNullWhen(true)] out WeightedCorner[][]? triangleSets,
			[NotNullWhen(true)] out WeightedMaterial[]? materials,
			[NotNullWhen(true)] out SortedSet<int>? usedWeightIndices)
		{
			SortedSet<ushort> usedVertices = [.. splitTriangleSets.SelectMany(x => x).Select(x => x.VertexIndex)];

			if(usedVertices.Count == 0)
			{
				vertices = null;
				triangleSets = null;
				materials = null;
				usedWeightIndices = null;
				return false;
			}

			vertices = new WeightedVertex[usedVertices.Count];
			Dictionary<ushort, ushort> vertexIndexMapping = [];
			usedWeightIndices = [];

			foreach((int index, ushort vertexIndex) in usedVertices.Index())
			{
				WeightedVertex vertex = mesh.Mesh.Vertices[vertexIndex];

				foreach((int weightIndex, float weight) in vertex.Weights!.Index())
				{
					if(weight > 0)
					{
						usedWeightIndices.Add(weightIndex);
					}
				}

				vertices[index] = vertex;

				vertexIndexMapping[vertexIndex] = (ushort)index;
			}

			List<WeightedCorner[]> resultTriangleSets = [];
			List<WeightedMaterial> resultMaterials = [];

			foreach((WeightedCorner[] splitCorners, WeightedMaterial material) in splitTriangleSets.Zip(mesh.Mesh.Materials))
			{
				if(splitCorners.Length == 0)
				{
					continue;
				}

				for(int c = 0; c < splitCorners.Length; c++)
				{
					splitCorners[c].VertexIndex = vertexIndexMapping[splitCorners[c].VertexIndex];
				}

				resultTriangleSets.Add(splitCorners);
				resultMaterials.Add(material);
			}

			triangleSets = [.. resultTriangleSets];
			materials = [.. resultMaterials];

			UpdateSplitVertexWeights(splitIndex, vertices, usedWeightIndices);

			return true;
		}

		private static void UpdateSplitVertexWeights(int splitIndex, WeightedVertex[] vertices, SortedSet<int> usedWeightIndices)
		{
			int weightMapSize = usedWeightIndices.Count(x => x != splitIndex);
			if(weightMapSize > 0)
			{
				int[] weightIndexMap = new int[weightMapSize + 1];
				weightIndexMap[0] = splitIndex;

				foreach((int j, int usedWeightIndex) in usedWeightIndices.Where(x => x != splitIndex).Index())
				{
					weightIndexMap[j + 1] = usedWeightIndex;
				}

				for(int v = 0; v < vertices.Length; v++)
				{
					WeightedVertex vertex = vertices[v];
					float[] oldWeights = vertex.Weights!;
					vertex.Weights = new float[weightIndexMap.Length];

					for(int w = 0; w < vertex.Weights.Length; w++)
					{
						vertex.Weights[w] = oldWeights[weightIndexMap[w]];
					}

					vertices[v] = vertex;
				}
			}
			else
			{
				for(int v = 0; v < vertices.Length; v++)
				{
					WeightedVertex vertex = vertices[v];
					vertex.Weights = null;
					vertices[v] = vertex;
				}
			}
		}

		#endregion

	}
}
