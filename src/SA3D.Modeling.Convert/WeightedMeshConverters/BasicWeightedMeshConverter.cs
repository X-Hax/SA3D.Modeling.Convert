using J113D.Common;
using J113D.Strippify;
using SA3D.Modeling.Convert.Weighted;
using SA3D.Modeling.Mesh;
using SA3D.Modeling.Mesh.Basic;
using SA3D.Modeling.Mesh.Basic.Polygon;
using SA3D.Modeling.ObjectData;
using SA3D.Modeling.ObjectData.Structs;
using SA3D.Modeling.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SA3D.Modeling.Convert.WeightedMeshConverters
{
	internal class BasicWeightedMeshConverter : WeightedMeshConverterBase
	{
		protected override bool AllowSplitWeighted => true;

		private readonly Dictionary<(int nodeIndex, MeshConversionInfo info), int[][]> _weightedVertexMaps = [];
		private readonly List<(int nodeIndex, MeshConversionInfo mesh, int weightIndex)> _dependentMeshes = [];

		private readonly List<Vector3> _positions = [];
		private readonly List<Vector3> _normals = [];
		private bool _hasNormals;
		private readonly List<int[]> _vertexMaps = [];

		private readonly List<BasicMeshSet> _meshSets = [];
		private readonly List<BasicMaterial> _materials = [];


		protected override void ConvertWeighted()
		{
			_weightedVertexMaps.Clear();
			if(Parameters.UseMetaWeights)
			{
				foreach((int index, MeshConversionInfo[] meshes) in NodeMeshes.Index())
				{
					foreach(MeshConversionInfo mesh in meshes)
					{
						if(mesh.Mesh.WeightNodeIndexMap != null)
						{
							_weightedVertexMaps.Add((index, mesh), new int[mesh.Mesh.WeightNodeIndexMap.Length + 1][]);
						}
					}
				}
			}

			foreach((int index, MeshConversionInfo[] meshes) in NodeMeshes.Index())
			{
				CollectDependentMeshes(index);

				if(meshes.Length == 0 && _dependentMeshes.Count == 0)
				{
					continue;
				}

				Node node = NodeWorldMatrices[index].node;
				BasicMesh basicMesh = ConvertToBasicMesh(meshes, node.Label, index);

				if(meshes.Any(x => !x.NoBounds))
				{
					basicMesh.RecalculateBounds();
				}

				node.MeshData = basicMesh;
			}

			if(Parameters.UseMetaWeights)
			{
				foreach((int index, MeshConversionInfo[] meshes) in NodeMeshes.Index())
				{
					foreach(MeshConversionInfo mesh in meshes)
					{
						if(mesh.Mesh.WeightNodeIndexMap != null)
						{
							SetupMetaWeights(index, mesh);
						}
					}
				}
			}
		}

		private BasicMesh ConvertToBasicMesh(MeshConversionInfo[] meshes, string nodeLabel, int nodeIndex)
		{
			string label = meshes.Length == 1 && !string.IsNullOrWhiteSpace(meshes[0].Mesh.Label) ? meshes[0].Mesh.Label! : nodeLabel + "_mesh";

			ConvertVertices(meshes, nodeIndex);
			ConvertPolygons(meshes, label);

			return new()
			{
				Label = label,
				Positions = new(label + "_positions", [.. _positions]),
				Normals = _normals.Count == 0 ? null : new(label + "_normals", [.. _normals]),
				MeshSets = new(label + "_sets", [.. _meshSets]),
				Materials = new(label + "_materials", [.. _materials])
			};
		}

		private void CollectDependentMeshes(int nodeIndex)
		{
			_dependentMeshes.Clear();
			if(!Parameters.UseMetaWeights)
			{
				return;
			}

			foreach((int iterNodeIndex, MeshConversionInfo[] iterMeshes) in NodeMeshes.Index())
			{
				if(iterNodeIndex == nodeIndex)
				{
					continue;
				}

				foreach(MeshConversionInfo mesh in iterMeshes)
				{
					if(mesh.Mesh.WeightNodeIndexMap == null)
					{
						continue;
					}

					int relativeNodeIndex = nodeIndex - iterNodeIndex;
					int weightIndex = Array.IndexOf(mesh.Mesh.WeightNodeIndexMap, relativeNodeIndex);
					if(weightIndex < 0)
					{
						continue;
					}

					_dependentMeshes.Add((iterNodeIndex, mesh, weightIndex + 1));
				}
			}
		}

		private void ConvertVertices(MeshConversionInfo[] meshes, int nodeIndex)
		{
			_positions.Clear();
			_normals.Clear();
			_vertexMaps.Clear();
			_hasNormals = meshes.Any(x => x.HasNormals) || _dependentMeshes.Any(x => x.mesh.HasNormals);

			foreach((int index, MeshConversionInfo mesh) in meshes.Index())
			{
				SortedSet<int> usedVertexIndices = [.. mesh.Mesh.TriangleSets.SelectMany(x => x).Select(x => x.VertexIndex)];
				int[] vertexMap = new int[mesh.Mesh.Vertices.Length];

				foreach(int vertexIndex in usedVertexIndices)
				{
					vertexMap[vertexIndex] = _positions.Count;
					WeightedVertex vertex = mesh.Mesh.Vertices[vertexIndex];
					_positions.Add(vertex.Position);

					if(_hasNormals)
					{
						if(mesh.HasNormals)
						{
							_normals.Add(vertex.Normal);
						}
						else
						{
							_normals.Add(WeightedVertex.DefaultNormal);
						}
					}
				}

				if(Parameters.UseMetaWeights && mesh.Mesh.WeightNodeIndexMap != null)
				{
					_weightedVertexMaps[(nodeIndex, mesh)][0] = vertexMap;
				}

				_vertexMaps.Add(vertexMap);
			}

			PrepareWeightedVertices(nodeIndex);
			MergeVertices();
		}

		private void PrepareWeightedVertices(int nodeIndex)
		{
			if(!Parameters.UseMetaWeights)
			{
				return;
			}

			foreach((int dependentNodeIndex, MeshConversionInfo mesh, int weightIndex) in _dependentMeshes)
			{
				Matrix4x4 toLocalSpaceMatrix = GetNodeSpaceTransform(dependentNodeIndex, nodeIndex);

				int[] dependentVertexMap = new int[mesh.Mesh.Vertices.Length];
				Array.Fill(dependentVertexMap, -1);

				foreach((int vertexIndex, WeightedVertex vertex) in mesh.Mesh.Vertices.Index())
				{
					if(vertex.Weights![weightIndex] <= 0)
					{
						continue;
					}

					dependentVertexMap[vertexIndex] = _positions.Count;

					_positions.Add(Vector3.Transform(vertex.Position, toLocalSpaceMatrix));
					if(_hasNormals)
					{
						if(mesh.HasNormals)
						{
							_normals.Add(Vector3.TransformNormal(vertex.Normal, toLocalSpaceMatrix));
						}
						else
						{
							_normals.Add(WeightedVertex.DefaultNormal);
						}
					}
				}

				_weightedVertexMaps[(dependentNodeIndex, mesh)][weightIndex] = dependentVertexMap;

				// The entries up until this point are for polygons.
				// After those (the ones we are adding now) are for knowing which vertices to add meta weights to.
				// We add them here so that vertex merging also updates these maps too
				_vertexMaps.Add(dependentVertexMap);

			}
		}

		private void MergeVertices()
		{
			if(!Parameters.MergeVertices)
			{
				return;
			}

			EqualityComparer<Vector3> positionComparer = VertexUtil.CreatePosition3EqualityComparer(Parameters.VertexMergeDistance);
			EqualityComparer<Vector3> normalComparer = VertexUtil.CreateNormal3EqualityComparer();
			int[]? map = null;

			if(_hasNormals)
			{
				(Vector3 pos, Vector3 nrm)[] vertices = [.. _positions.Zip(_normals)];
				EqualityComparer<(Vector3 pos, Vector3 nrm)> comparer = EqualityComparer<(Vector3 pos, Vector3 nrm)>.Create(
					(a, b) => positionComparer.Equals(a.pos, b.pos) && normalComparer.Equals(a.pos, b.pos)
				);

				if(DistinctMap<(Vector3 pos, Vector3 nrm)>.TryCreateDistinctMapSSP(
					vertices,
					v => v.pos.GetVector3SSP(),
					Parameters.VertexMergeDistance,
					comparer,
					out DistinctMap<(Vector3 pos, Vector3 nrm)> distinctMap))
				{
					_positions.Clear();
					_positions.AddRange(distinctMap.Values.Select(x => x.pos));

					_normals.Clear();
					_normals.AddRange(distinctMap.Values.Select(x => x.nrm));

					map = distinctMap.Map;
				}
			}
			else
			{
				if(DistinctMap<Vector3>.TryCreateDistinctMapSSP(
					_positions,
					VertexUtil.GetVector3SSP,
					Parameters.VertexMergeDistance,
					positionComparer,
					out DistinctMap<Vector3> positionMap))
				{
					_positions.Clear();
					_positions.AddRange(positionMap.Values);
					map = positionMap.Map;
				}
			}

			if(map != null)
			{
				foreach(int[] vertexMap in _vertexMaps)
				{
					for(int i = 0; i < vertexMap.Length; i++)
					{
						int index = vertexMap[i];
						if(index >= 0)
						{
							vertexMap[i] = map[vertexMap[i]];
						}
					}
				}
			}
		}

		private void ConvertPolygons(MeshConversionInfo[] meshes, string meshLabel)
		{
			_meshSets.Clear();
			_materials.Clear();

			foreach((MeshConversionInfo mesh, int[] vertexMap) in meshes.Zip(_vertexMaps))
			{
				foreach((WeightedCorner[] triangles, WeightedMaterial material) in mesh.Mesh.TriangleSets.Zip(mesh.Mesh.Materials))
				{
					WeightedCorner[] mappedTriangles = [.. triangles.Select(x =>
					{
						WeightedCorner result = x;
						x.VertexIndex = (ushort)vertexMap[x.VertexIndex];
						return x;
					})];

					ReversableStrip<WeightedCorner>[] strips = TriangleStrippifier.Global.StrippifyReversable(mappedTriangles, c => c.VertexIndex, 1);

					int trianglesByteSize = mappedTriangles.Length * 2;
					int stripsByteSize = (strips.Length + strips.Sum(x => x.Corners.Length)) * 2;

					BasicPolygonType type;
					IBasicPolygon[] polygons;
					IEnumerable<WeightedCorner> cornerEnumerable;

					if(trianglesByteSize < stripsByteSize)
					{
						type = BasicPolygonType.Triangles;
						polygons = new IBasicPolygon[triangles.Length / 3];
						cornerEnumerable = mappedTriangles;

						for(int i = 0, r = 0; i < triangles.Length; i += 3, r++)
						{
							polygons[r] = new BasicTriangle(
								triangles[i].VertexIndex,
								triangles[i].VertexIndex,
								triangles[i].VertexIndex
							);
						}
					}
					else
					{
						type = BasicPolygonType.TriangleStrips;
						polygons = [.. strips.Select(x => (IBasicPolygon)new BasicMultiPolygon([.. x.Corners.Select(x => x.VertexIndex)], x.Reverse))];
						cornerEnumerable = strips.SelectMany(x => x.Corners);
					}

					Color[]? colors = mesh.HasDiffuseColors ? [.. cornerEnumerable.Select(x => x.Diffuse)] : null;
					Vector2[]? texcoords = (material.UseTexture && !material.EnvironmentMapping) ? [.. cornerEnumerable.Select(x => x.TexCoord)] : null;

					string meshSetLabel = meshLabel + "_set" + _meshSets.Count;

					_meshSets.Add(new()
					{
						MaterialIndex = (ushort)_materials.Count,
						PolygonType = type,
						Polygons = new(meshSetLabel + "_polygons", polygons),
						Colors = colors == null ? null : new(meshSetLabel + "_colors", colors),
						TextureCoordinates = texcoords == null ? null : new(meshSetLabel + "_uvs", texcoords)
					});

					_materials.Add(new()
					{
						DiffuseColor = material.Diffuse,
						SpecularColor = material.Specular,
						SpecularExponent = material.SpecularExponent,
						TextureID = material.TextureIndex,
						MipmapDistanceMultiplier = material.MipmapDistanceMultiplier,
						SuperSample = material.AnisotropicFiltering,
						FilterMode = material.TextureFilterMode,
						ClampV = material.ClampV,
						ClampU = material.ClampU,
						MirrorV = material.MirrorV,
						MirrorU = material.MirrorU,
						IgnoreSpecular = material.IgnoreSpecular,
						UseAlpha = material.AlphaMode != AlphaMode.Opaque,
						UseTexture = material.UseTexture,
						EnvironmentMap = material.EnvironmentMapping,
						DoubleSided = material.DoubleSided,
						FlatShading = material.FlatShading,
						IgnoreLighting = material.IgnoreLighting,
						DestinationAlpha = material.DestinationBlendMode,
						SourceAlpha = material.SourceBlendMode
					});
				}

			}
		}

		private void SetupMetaWeights(int nodeIndex, MeshConversionInfo mesh)
		{
			Node node = NodeWorldMatrices[nodeIndex].node;

			int[][] vertexMaps = _weightedVertexMaps[(nodeIndex, mesh)];

			Node[] nodeMap = new Node[vertexMaps.Length];
			nodeMap[0] = node;
			for(int i = 1; i < nodeMap.Length; i++)
			{
				nodeMap[i] = NodeWorldMatrices[nodeIndex + mesh.Mesh.WeightNodeIndexMap![i - 1]].node;
			}

			List<VertexWelding> weldings = [];

			foreach((int index, WeightedVertex vertex) in mesh.Mesh.Vertices.Index())
			{
				if(vertex.Weights![0] >= 1)
				{
					continue;
				}

				List<Weld> welds = [];

				for(int w = 0; w < vertex.Weights.Length; w++)
				{
					float weight = vertex.Weights[w];
					if(weight > 0)
					{
						welds.Add(new(nodeMap[w], (uint)vertexMaps[w][index], weight));
					}
				}

				weldings.Add(new((uint)vertexMaps[0][index], [.. welds]));
			}

			node.Welding = [.. weldings];
		}
	}
}
