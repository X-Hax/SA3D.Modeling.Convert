using J113D.Strippify;
using SA3D.Common;
using SA3D.Modeling.Convert.Weighted;
using SA3D.Modeling.Mesh.Chunk;
using SA3D.Modeling.Mesh.Chunk.PolyChunks;
using SA3D.Modeling.Mesh.Chunk.Structs;
using SA3D.Modeling.ObjectData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace SA3D.Modeling.Convert.WeightedMeshConverters
{
	internal class ChunkWeightedMeshConverter : WeightedMeshConverterBase
	{
		protected override bool AllowSplitWeighted => false;

		private readonly struct ChunkMeshSet
		{
			public readonly ushort vertexCount;
			public readonly VertexChunk[][] vertexChunksPerNode;
			public readonly PolyChunk[] polygonChunks;

			public ChunkMeshSet(ushort vertexCount, VertexChunk[][] vertexChunksPerNode, PolyChunk[] polygonChunks)
			{
				this.vertexCount = vertexCount;
				this.vertexChunksPerNode = vertexChunksPerNode;
				this.polygonChunks = polygonChunks;
			}
		}

		private readonly struct BufferBlock
		{
			public readonly string name;
			public readonly int index;
			public readonly int width;
			public readonly int height;
			public readonly int yOffset;

			public BufferBlock(string name, int index, int width, int height, int yOffset)
			{
				this.name = name;
				this.index = index;
				this.width = width;
				this.height = height;
				this.yOffset = yOffset;
			}
		}

		private SortedSet<ushort>[] _vertexNodeAssignments = [];
		private int[] _vertexStartNodes = [];
		private int[] _vertexEndNodes = [];

		private ChunkCorner[][] _chunkTriangleSets = [];
		private (int, ChunkVertex)[] _expandableChunkVertices = [];
		private ushort[] _expandableChunkVertexOffsets = [];
		private ushort[] _vertexIndexMapping = [];
		private int _expandedUnweightedNodeCount;

		private int[] _weightNodeIndexMap = [];
		private int[] _weightIndexOrder = [];

		protected override void ConvertWeighted()
		{
			Dictionary<MeshConversionInfo, ChunkMeshSet> rawChunkMeshSets = [];
			foreach(MeshConversionInfo mesh in Meshes)
			{
				ChunkMeshSet meshSet = ConvertToChunkMeshSet(mesh);
				if(meshSet.polygonChunks.Length > 0)
				{
					rawChunkMeshSets.Add(mesh, meshSet);
				}
			}

			Dictionary<int, List<(int bufferIndex, VertexChunk[])>> nodeVertexChunkLists = [];
			Dictionary<int, List<(int bufferIndex, PolyChunk[])>> nodePolyChunkLists = [];
			int currentBufferIndex = 0;
			List<BufferBlock> bufferBlocks = [];

			foreach((int index, MeshConversionInfo[] meshes) in NodeMeshes.Index())
			{
				if(meshes.Length == 0)
				{
					continue;
				}

				foreach(MeshConversionInfo mesh in meshes)
				{
					if(!rawChunkMeshSets.TryGetValue(mesh, out ChunkMeshSet meshSet))
					{
						continue;
					}

					int[] nodeIndices = [index, .. (mesh.Mesh.WeightNodeIndexMap ?? []).Select(x => x + index)];
					foreach((int weightIndex, VertexChunk[] vertexChunks) in meshSet.vertexChunksPerNode.Index())
					{
						if(vertexChunks.Length == 0)
						{
							continue;
						}

						int vertexNodeIndex = nodeIndices[weightIndex];
						if(!nodeVertexChunkLists.TryGetValue(vertexNodeIndex, out List<(int bufferIndex, VertexChunk[])>? nodeVertexChunkList))
						{
							nodeVertexChunkList = [];
							nodeVertexChunkLists.Add(vertexNodeIndex, nodeVertexChunkList);
						}

						VertexChunk[] nodeVertexChunks = vertexChunks.ContentClone();
						if(weightIndex != 0)
						{
							Matrix4x4 toLocalSpaceMatrix = GetNodeSpaceTransform(index, vertexNodeIndex);
							foreach(VertexChunk vertexChunk in nodeVertexChunks)
							{
								bool hasNormals = vertexChunk.Type.CheckHasNormal();

								for(int i = 0; i < vertexChunk.Vertices.Length; i++)
								{
									ChunkVertex vertex = vertexChunk.Vertices[i];

									vertex.Position = Vector3.Transform(vertex.Position, toLocalSpaceMatrix);

									if(hasNormals)
									{
										vertex.Normal = Vector3.TransformNormal(vertex.Normal, toLocalSpaceMatrix);
									}

									vertexChunk.Vertices[i] = vertex;
								}
							}
						}

						nodeVertexChunkList.Add((currentBufferIndex, nodeVertexChunks));
					}

					int firstNode = nodeIndices.Min();
					int lastNode = nodeIndices.Max();
					if(!nodePolyChunkLists.TryGetValue(lastNode, out List<(int bufferIndex, PolyChunk[])>? nodePolyChunkList))
					{
						nodePolyChunkList = [];
						nodePolyChunkLists.Add(lastNode, nodePolyChunkList);
					}

					nodePolyChunkList.Add((currentBufferIndex, meshSet.polygonChunks.ContentClone()));
					bufferBlocks.Add(new(mesh.Mesh.Label ?? $"Mesh{currentBufferIndex}", currentBufferIndex, meshSet.vertexCount, lastNode - firstNode + 1, firstNode));

					currentBufferIndex++;
				}
			}

			int[] bufferOffsets = DetermineBufferOffsets(bufferBlocks);
			UpdateChunkVertexOffsets(
				nodeVertexChunkLists.SelectMany(x => x.Value),
				nodePolyChunkLists.SelectMany(x => x.Value),
				bufferOffsets
			);

			foreach((int index, (Node node, _)) in NodeWorldMatrices.Index())
			{
				bool calculateBounds = false;
				List<int> usedBufferIndices = [];

				VertexChunkArray? vertexChunks = null;
				if(nodeVertexChunkLists.TryGetValue(index, out List<(int bufferIndex, VertexChunk[])>? nodeVertexChunks))
				{
					vertexChunks = new([.. nodeVertexChunks.SelectMany(x => x.Item2)]);
					usedBufferIndices.AddRange(nodeVertexChunks.Select(x => x.bufferIndex));
				}

				PolyChunkArray? polyChunks = null;
				if(nodePolyChunkLists.TryGetValue(index, out List<(int bufferIndex, PolyChunk[])>? nodePolyChunks))
				{
					polyChunks = new([.. nodePolyChunks.SelectMany(x => x.Item2)]);
					usedBufferIndices.AddRange(nodePolyChunks.Select(x => x.bufferIndex));
				}

				if(vertexChunks != null || polyChunks != null)
				{
					node.MeshData = new ChunkMesh
					{
						Label = usedBufferIndices.Count > 1
							? $"{node.Label}_Mesh"
							: bufferBlocks[usedBufferIndices[0]].name,

						VertexChunks = vertexChunks,
						PolyChunks = polyChunks
					};

					vertexChunks?.Label = node.MeshData.Label + "_vertices";
					polyChunks?.Label = node.MeshData.Label + "_polys";

					if(calculateBounds)
					{
						node.MeshData.RecalculateBounds();
					}
				}
			}
		}

		private ChunkMeshSet ConvertToChunkMeshSet(MeshConversionInfo mesh)
		{
			_weightNodeIndexMap = [0, .. mesh.Mesh.WeightNodeIndexMap ?? []];

			int[] weightNodeIndices = (int[])_weightNodeIndexMap.Clone();
			Array.Sort(weightNodeIndices);
			_weightIndexOrder = [.. _weightNodeIndexMap.Select(x => Array.IndexOf(weightNodeIndices, x))];

			AssignVerticesToNodes(mesh);
			ExpandVerticesFromCornerData(mesh);


			VertexChunk[][] vertexChunks = _expandedUnweightedNodeCount < _expandableChunkVertices.Length
				? CompileWeightedChunkVertices(mesh)
				: CompileWeightlessChunkVertices(mesh);

			foreach(ChunkCorner[] triangleSet in _chunkTriangleSets)
			{
				for(int i = 0; i < triangleSet.Length; i++)
				{
					triangleSet[i].Index = _vertexIndexMapping[triangleSet[i].Index];
				}
			}

			PolyChunk[] polychunks = mesh.VolumeMesh
				? CompileVolumePolyChunks(mesh)
				: CompilePolyChunks(mesh);

			return new((ushort)_expandableChunkVertices.Length, vertexChunks, polychunks);

		}

		private void AssignVerticesToNodes(MeshConversionInfo mesh)
		{
			_vertexStartNodes = new int[mesh.Mesh.Vertices.Length];
			_vertexEndNodes = new int[mesh.Mesh.Vertices.Length];

			SortedSet<ushort> usedVertexIndices = [.. mesh.Mesh.TriangleSets.SelectMany(x => x).Select(x => x.VertexIndex)];

			if(mesh.Mesh.WeightNodeIndexMap == null)
			{
				_vertexNodeAssignments = [usedVertexIndices];
				return;
			}

			_vertexNodeAssignments = new SortedSet<ushort>[mesh.Mesh.WeightNodeIndexMap!.Length + 1];
			for(int i = 0; i < _vertexNodeAssignments.Length; i++)
			{
				_vertexNodeAssignments[i] = [];
			}

			if(mesh.ChunkVertexType.CheckHasAttributes())
			{
				// half since we will round values when converting, not truncating
				float minWeight = Parameters.HighPrecisionWeights
					? 0.5f / ushort.MaxValue
					: 0.5f / byte.MaxValue;

				foreach((int vertexIndex, WeightedVertex vertex) in usedVertexIndices.Select(x => (x, mesh.Mesh.Vertices[x])))
				{
					int startNode = int.MaxValue;
					int endNode = int.MinValue;

					foreach((int weightIndex, float weight) in vertex.Weights!.Index())
					{
						if(weight > minWeight)
						{
							_vertexNodeAssignments[weightIndex].Add((ushort)vertexIndex);
							int weightNodeIndex = _weightNodeIndexMap[weightIndex];

							if(weightNodeIndex < startNode)
							{
								startNode = weightNodeIndex;
							}

							if(weightNodeIndex > endNode)
							{
								endNode = weightNodeIndex;
							}
						}
					}

					_vertexStartNodes[vertexIndex] = startNode;
					_vertexEndNodes[vertexIndex] = endNode;
				}
			}
			else
			{
				foreach((int vertexIndex, WeightedVertex vertex) in usedVertexIndices.Select(x => (x, mesh.Mesh.Vertices[x])))
				{
					float maxWeight = 0;
					int maxWeightIndex = 0;

					foreach((int weightIndex, float weight) in vertex.Weights!.Index())
					{
						if(weight > maxWeight)
						{
							maxWeight = weight;
							maxWeightIndex = weightIndex;
						}
					}

					_vertexNodeAssignments[maxWeightIndex].Add((ushort)vertexIndex);
					_vertexStartNodes[vertexIndex] = _vertexEndNodes[vertexIndex] = _weightNodeIndexMap[maxWeightIndex];
				}
			}
		}

		private void ExpandVerticesFromCornerData(MeshConversionInfo mesh)
		{
			_chunkTriangleSets = [.. mesh.Mesh.TriangleSets.Select(x => new ChunkCorner[x.Length])];
			_expandedUnweightedNodeCount = 0;

			_expandableChunkVertexOffsets = new ushort[mesh.Mesh.Vertices.Length];
			Array.Fill(_expandableChunkVertexOffsets, ushort.MaxValue);

			List<(int, ChunkVertex)> expandableVertices = [];
			List<(int, ChunkVertex)> currentExpandableVertices = [];
			int currentVertexIndex = 0;

			Func<ChunkVertex, ChunkVertex, bool>? areCornersEqual;
			Func<WeightedCorner, ChunkVertex> convertToExpandableVertex = convertToExpandableVertex = c => new()
			{
				Diffuse = c.Diffuse,
				Specular = c.Specular
			};

			switch(mesh.ChunkVertexType)
			{
				case VertexChunkType.BlankVec4:
				case VertexChunkType.NormalVec4:
				case VertexChunkType.Blank:
				case VertexChunkType.Attributes:
				case VertexChunkType.Normal:
				case VertexChunkType.UserAttributes:
				case VertexChunkType.NormalUserAttributes:
				case VertexChunkType.NormalAttributes:
				case VertexChunkType.Normal32:
				case VertexChunkType.Normal32UserAttributes:
					// No information transferred from corners to vertices, so we dont even need to perform this whole logic
					areCornersEqual = null;
					convertToExpandableVertex = c => default;
					break;

				case VertexChunkType.Diffuse:
				case VertexChunkType.NormalDiffuse:
				case VertexChunkType.Normal32Diffuse:
				case VertexChunkType.AttributesDiffuse:
					areCornersEqual = (a, b) => a.Diffuse.ARGB == b.Diffuse.ARGB;
					convertToExpandableVertex = c => new()
					{
						Diffuse = c.Diffuse
					};
					break;

				case VertexChunkType.DiffuseSpecular5:
				case VertexChunkType.NormalDiffuseSpecular5:
					areCornersEqual = (a, b) => a.Diffuse.RGB565 == b.Diffuse.RGB565 && a.Specular.RGB565 == b.Specular.RGB565;
					break;

				case VertexChunkType.DiffuseSpecular4:
				case VertexChunkType.NormalDiffuseSpecular4:
					areCornersEqual = (a, b) => a.Diffuse.ARGB4 == b.Diffuse.ARGB4 && a.Specular.RGB565 == b.Specular.RGB565;
					break;

				case VertexChunkType.DiffuseSpecular:
					areCornersEqual = (a, b) => a.Diffuse.ARGB == b.Diffuse.ARGB && a.Specular.ARGB == b.Specular.ARGB;
					break;

				case VertexChunkType.Intensity:
				case VertexChunkType.NormalIntensity:
					areCornersEqual = (a, b) =>
						ChunkVertex.ColorToIntensity(a.Diffuse) == ChunkVertex.ColorToIntensity(b.Diffuse)
						&& ChunkVertex.ColorToIntensity(a.Specular) == ChunkVertex.ColorToIntensity(b.Specular);
					break;
				default:
					throw new InvalidOperationException();
			}

			IOrderedEnumerable<(int setIndex, int cornerIndex, WeightedCorner corner)> evalCorners = mesh.Mesh.TriangleSets.SelectMany((set, setIndex) => set.Select((corner, cornerIndex) => (setIndex, cornerIndex, corner))).OrderBy(x => x.corner.VertexIndex);
			foreach((int setIndex, int cornerIndex, WeightedCorner corner) in evalCorners)
			{
				if(corner.VertexIndex != currentVertexIndex)
				{
					if(currentExpandableVertices.Count > 0)
					{
						_expandableChunkVertexOffsets[currentVertexIndex] = (ushort)expandableVertices.Count;

						if(_vertexStartNodes[currentVertexIndex] == _vertexEndNodes[currentVertexIndex])
						{
							_expandedUnweightedNodeCount += currentExpandableVertices.Count;
						}
					}

					expandableVertices.AddRange(currentExpandableVertices);
					currentExpandableVertices.Clear();
					currentVertexIndex = corner.VertexIndex;
				}

				ChunkVertex expandableVertex = convertToExpandableVertex(corner);
				ushort count = (ushort)(expandableVertices.Count + currentExpandableVertices.Count);
				ushort index = count;

				if(areCornersEqual != null)
				{
					foreach((int cacheIndex, ChunkVertex cacheVertex) in currentExpandableVertices)
					{
						if(areCornersEqual(cacheVertex, expandableVertex))
						{
							index = (ushort)(expandableVertices.Count + cacheIndex);
							break;
						}
					}
				}
				else if(currentExpandableVertices.Count > 0)
				{
					index--;
				}

				if(index == count)
				{
					currentExpandableVertices.Add(new(currentVertexIndex, expandableVertex));
				}

				_chunkTriangleSets[setIndex][cornerIndex] = new()
				{
					Index = index,
					Texcoord = corner.TexCoord
				};
			}

			if(currentExpandableVertices.Count > 0)
			{
				_expandableChunkVertexOffsets[currentVertexIndex] = (ushort)expandableVertices.Count;

				if(_vertexStartNodes[currentVertexIndex] == _vertexEndNodes[currentVertexIndex])
				{
					_expandedUnweightedNodeCount += currentExpandableVertices.Count;
				}
			}

			expandableVertices.AddRange(currentExpandableVertices);

			_expandableChunkVertices = [.. expandableVertices];
		}

		private VertexChunk[][] CompileWeightedChunkVertices(MeshConversionInfo mesh)
		{
			_vertexIndexMapping = new ushort[_expandableChunkVertices.Length];
			Array.Fill(_vertexIndexMapping, ushort.MaxValue);

			VertexChunk[][] result = new VertexChunk[_weightIndexOrder.Length][];

			int unweightedOffset = 0;
			ushort weightedOffset = (ushort)_expandedUnweightedNodeCount;
			bool hasNormals = mesh.ChunkVertexType.CheckHasNormal();

			VertexChunkType unweightedChunkType = mesh.ChunkVertexType switch
			{
				VertexChunkType.Attributes => VertexChunkType.Blank,
				VertexChunkType.NormalAttributes => VertexChunkType.Normal,
				VertexChunkType.AttributesDiffuse => VertexChunkType.Diffuse,
				_ => throw new UnreachableException()
			};

			VertexChunkType weightedChunkType = mesh.ChunkVertexType switch
			{
				VertexChunkType.AttributesDiffuse => VertexChunkType.Attributes,
				_ => mesh.ChunkVertexType
			};

			foreach(int weightIndex in _weightIndexOrder)
			{
				int nodeIndex = _weightNodeIndexMap[weightIndex];
				List<ChunkVertex> unweightedVertices = [];
				List<ChunkVertex> startVertices = [];
				List<ChunkVertex> middleVertices = [];
				List<ChunkVertex> endVertices = [];

				foreach(ushort vertexIndex in _vertexNodeAssignments[weightIndex])
				{
					WeightedVertex vertex = mesh.Mesh.Vertices[vertexIndex];

					float weight = vertex.Weights![weightIndex];
					int startNode = _vertexStartNodes[vertexIndex];
					int endNode = _vertexEndNodes[vertexIndex];

					for(ushort expandedVertexIndex = _expandableChunkVertexOffsets[vertexIndex];
						expandedVertexIndex < _expandableChunkVertices.Length && _expandableChunkVertices[expandedVertexIndex].Item1 == vertexIndex;
						expandedVertexIndex++)
					{
						ChunkVertex chunkVertex = _expandableChunkVertices[expandedVertexIndex].Item2;
						chunkVertex.Position = vertex.Position;

						if(hasNormals)
						{
							chunkVertex.Normal = vertex.Normal;
						}

						if(startNode == endNode)
						{
							_vertexIndexMapping[expandedVertexIndex] = (ushort)(unweightedOffset + unweightedVertices.Count);
							unweightedVertices.Add(chunkVertex);
							continue;
						}

						if(Parameters.HighPrecisionWeights)
						{
							chunkVertex.Weight2 = weight;
						}
						else
						{
							chunkVertex.Weight = weight;
						}

						chunkVertex.Index = _vertexIndexMapping[expandedVertexIndex];
						if(chunkVertex.Index == ushort.MaxValue)
						{
							_vertexIndexMapping[expandedVertexIndex] = chunkVertex.Index = weightedOffset;
							weightedOffset++;
						}

						if(startNode == nodeIndex)
						{
							startVertices.Add(chunkVertex);
						}
						else if(endNode == nodeIndex)
						{
							endVertices.Add(chunkVertex);
						}
						else
						{
							middleVertices.Add(chunkVertex);
						}
					}
				}

				List<VertexChunk> nodeVertexChunks = [];

				if(unweightedVertices.Count > 0)
				{
					nodeVertexChunks.Add(new()
					{
						Type = unweightedChunkType,
						IndexOffset = (ushort)unweightedOffset,
						Vertices = [.. unweightedVertices]
					});
					unweightedOffset += unweightedVertices.Count;
				}

				if(startVertices.Count > 0)
				{
					nodeVertexChunks.Add(new()
					{
						Type = weightedChunkType,
						WeightMode = WeightMode.Start,
						Vertices = [.. startVertices]
					});
				}

				if(middleVertices.Count > 0)
				{
					nodeVertexChunks.Add(new()
					{
						Type = weightedChunkType,
						WeightMode = WeightMode.Middle,
						Vertices = [.. middleVertices]
					});
				}

				if(endVertices.Count > 0)
				{
					nodeVertexChunks.Add(new()
					{
						Type = mesh.ChunkVertexType,
						WeightMode = WeightMode.End,
						Vertices = [.. endVertices]
					});
				}

				result[weightIndex] = [.. nodeVertexChunks];
			}

			return [.. result.Select(x => x.ToArray())];
		}

		private VertexChunk[][] CompileWeightlessChunkVertices(MeshConversionInfo mesh)
		{
			_vertexIndexMapping = new ushort[_expandableChunkVertices.Length];
			Array.Fill(_vertexIndexMapping, ushort.MaxValue);

			VertexChunk[][] result = new VertexChunk[_weightIndexOrder.Length][];

			ushort vertexOffset = 0;
			bool hasNormals = mesh.ChunkVertexType.CheckHasNormal();
			bool hasUserAttributes = mesh.ChunkVertexType.CheckHasUserAttributes();

			foreach(int weightIndex in _weightIndexOrder)
			{
				List<ChunkVertex> chunkVertices = [];

				foreach(ushort vertexIndex in _vertexNodeAssignments[weightIndex])
				{
					WeightedVertex vertex = mesh.Mesh.Vertices[vertexIndex];

					for(ushort expandedVertexIndex = _expandableChunkVertexOffsets[vertexIndex];
						expandedVertexIndex < _expandableChunkVertices.Length && _expandableChunkVertices[expandedVertexIndex].Item1 == vertexIndex;
						expandedVertexIndex++)
					{
						_vertexIndexMapping[expandedVertexIndex] = (ushort)(vertexOffset + chunkVertices.Count);
						ChunkVertex chunkVertex = _expandableChunkVertices[expandedVertexIndex].Item2;
						chunkVertex.Position = vertex.Position;

						if(hasNormals)
						{
							chunkVertex.Normal = vertex.Normal;
						}

						if(hasUserAttributes)
						{
							chunkVertex.Attributes = vertex.UserAttributes;
						}

						chunkVertices.Add(chunkVertex);
					}
				}

				if(chunkVertices.Count == 0)
				{
					result[weightIndex] = [];
					continue;
				}

				result[weightIndex] = [
					new()
					{
						Type = mesh.ChunkVertexType,
						IndexOffset = vertexOffset,
						Vertices = [.. chunkVertices],
					}
				];

				vertexOffset += (ushort)chunkVertices.Count;
			}

			return result;
		}

		private PolyChunk[] CompileVolumePolyChunks(MeshConversionInfo mesh)
		{
			List<PolyChunk> result = [];

			foreach((ChunkCorner[] triangleSet, WeightedMaterial material) in _chunkTriangleSets.Zip(mesh.Mesh.Materials))
			{
				if(triangleSet.Length == 0)
				{
					continue;
				}

				IChunkVolumePolygon[] triangles = new IChunkVolumePolygon[triangleSet.Length / 3];
				bool hasAttr1 = material.PolygonAttributeCount > 0;
				bool hasAttr2 = material.PolygonAttributeCount > 1;
				bool hasAttr3 = material.PolygonAttributeCount > 2;

				for(int i = 0, o = 0; i < triangleSet.Length; i += 3, o++)
				{
					ChunkCorner lastCorner = triangleSet[i + 2];

					ChunkVolumeTriangle triangle = new()
					{
						Index1 = triangleSet[i].Index,
						Index2 = triangleSet[i + 1].Index,
						Index3 = lastCorner.Index
					};

					if(hasAttr1)
					{
						triangle.Attribute1 = lastCorner.Attributes1;
						if(hasAttr2)
						{
							triangle.Attribute2 = lastCorner.Attributes2;
							if(hasAttr3)
							{
								triangle.Attribute3 = lastCorner.Attributes3;
							}
						}
					}

					triangles[o] = triangle;
				}

				result.Add(new VolumeChunk()
				{
					Polygons = triangles,
					PolygonAttributeCount = material.PolygonAttributeCount
				});
			}

			return [.. result];
		}

		private PolyChunk[] CompilePolyChunks(MeshConversionInfo mesh)
		{
			List<PolyChunk> result = [];
			WeightedMaterial activeMaterial = default;
			bool initAmbient = false;
			bool initSpecular = false;
			bool initTexture = false;

			foreach((ChunkCorner[] triangles, WeightedMaterial material) in _chunkTriangleSets.Zip(mesh.Mesh.Materials))
			{
				if(triangles.Length == 0)
				{
					continue;
				}

				bool first = result.Count == 0;

				// ========================= Material Properties =========================

				byte specularExponent = (byte)float.Round(material.SpecularExponent);

				MaterialChunk materialChunk = new()
				{
					SourceBlendMode = material.SourceBlendMode,
					DestinationBlendMode = material.DestinationBlendMode
				};

				if(first || activeMaterial.Diffuse.ARGB != material.Diffuse.ARGB)
				{
					materialChunk.Diffuse = activeMaterial.Diffuse = material.Diffuse;
				}

				if(!activeMaterial.IgnoreAmbient && (!initAmbient || activeMaterial.Ambient.ARGB != material.Ambient.ARGB))
				{
					materialChunk.Ambient = activeMaterial.Ambient = material.Ambient;
					initAmbient = true;
				}

				if(!activeMaterial.IgnoreSpecular && (!initSpecular || activeMaterial.Specular.ARGB != material.Specular.ARGB))
				{
					materialChunk.Specular = activeMaterial.Ambient = material.Specular;
					materialChunk.SpecularExponent = specularExponent;
					activeMaterial.SpecularExponent = material.SpecularExponent;
					initSpecular = true;
				}

				if(materialChunk.Type != PolyChunkType.Material_Empty)
				{
					result.Add(materialChunk);

					activeMaterial.SourceBlendMode = material.SourceBlendMode;
					activeMaterial.DestinationBlendMode = material.DestinationBlendMode;
				}
				else
				{
					byte prevExponent = (byte)float.Round(activeMaterial.SpecularExponent);

					if(!material.IgnoreSpecular && specularExponent != prevExponent)
					{
						result.Add(new SpecularExponentChunk()
						{
							SpecularExponent = specularExponent
						});

						activeMaterial.SpecularExponent = material.SpecularExponent;
					}

					if(material.AlphaMode != Mesh.AlphaMode.Opaque && (
							material.SourceBlendMode != activeMaterial.SourceBlendMode
							|| material.DestinationBlendMode != activeMaterial.DestinationBlendMode))
					{
						result.Add(new BlendAlphaChunk()
						{
							SourceBlendMode = material.SourceBlendMode,
							DestinationBlendMode = material.DestinationBlendMode
						});

						activeMaterial.SourceBlendMode = material.SourceBlendMode;
						activeMaterial.DestinationBlendMode = material.DestinationBlendMode;
					}
				}

				// ========================= Texture Properties =========================

				if(material.UseTexture)
				{
					// Using the chunks to convert the mipmap distance multiplier
					MipmapDistanceMultiplierChunk mdm = new() { MipmapDistanceMultiplier = material.MipmapDistanceMultiplier };

					if(initTexture
						|| activeMaterial.ClampV != material.ClampV
						|| activeMaterial.ClampU != material.ClampU
						|| activeMaterial.MirrorV != material.MirrorV
						|| activeMaterial.MirrorU != material.MirrorU
						|| activeMaterial.TextureIndex != material.TextureIndex
						|| activeMaterial.AnisotropicFiltering != material.AnisotropicFiltering
						|| activeMaterial.TextureFilterMode != material.TextureFilterMode)
					{
						result.Add(new TextureChunk()
						{
							MipmapDistanceMultiplier = mdm.MipmapDistanceMultiplier,
							ClampV = material.ClampV,
							ClampU = material.ClampU,
							MirrorV = material.MirrorV,
							MirrorU = material.MirrorU,
							TextureID = (ushort)material.TextureIndex,
							SuperSample = material.AnisotropicFiltering,
							FilterMode = material.TextureFilterMode
						});

						activeMaterial.ClampV = material.ClampV;
						activeMaterial.ClampU = material.ClampU;
						activeMaterial.MirrorV = material.MirrorV;
						activeMaterial.MirrorU = material.MirrorU;
						activeMaterial.TextureIndex = material.TextureIndex;
						activeMaterial.AnisotropicFiltering = material.AnisotropicFiltering;
						activeMaterial.TextureFilterMode = material.TextureFilterMode;
						activeMaterial.MipmapDistanceMultiplier = mdm.MipmapDistanceMultiplier;

						initTexture = true;
					}
					else if(activeMaterial.MipmapDistanceMultiplier != mdm.MipmapDistanceMultiplier)
					{
						result.Add(mdm);
						activeMaterial.MipmapDistanceMultiplier = mdm.MipmapDistanceMultiplier;
					}
				}
				else
				{
					// ensuring no UVs
					for(int i = 0; i < triangles.Length; i++)
					{
						triangles[i].Texcoord = default;
						triangles[i].Texcoord2 = default;
					}
				}

				// ========================= Polygon Properties =========================

				ChunkStrip[] strips = [
					.. TriangleStrippifier.Global.StrippifyReversable(triangles, c => c.Index, 1)
						.Select(x => new ChunkStrip(x.Corners, x.Reverse))
				];

				StripChunk stripChunk = new()
				{
					AlphaMode = material.AlphaMode,
					DoubleSide = material.DoubleSided,
					EnvironmentMapping = material.EnvironmentMapping,
					FlatShading = material.FlatShading,
					IgnoreAmbient = material.IgnoreAmbient,
					IgnoreLight = material.IgnoreLighting,
					IgnoreSpecular = material.IgnoreSpecular,
					Strips = strips
				};

				stripChunk.ChangeType(
					!material.UseTexture
					? PolyChunkType.Strip_Blank
					: material.TexcoordPrecisionLevel >= 2
					? PolyChunkType.Strip_HDTex
					: PolyChunkType.Strip_Tex
				);

				result.Add(stripChunk);

				activeMaterial.AlphaMode = material.AlphaMode;
				activeMaterial.DoubleSided = material.DoubleSided;
				activeMaterial.EnvironmentMapping = material.EnvironmentMapping;
				activeMaterial.FlatShading = material.FlatShading;
				activeMaterial.IgnoreAmbient = material.IgnoreAmbient;
				activeMaterial.IgnoreLighting = material.IgnoreLighting;
				activeMaterial.IgnoreSpecular = material.IgnoreSpecular;
			}

			return [.. result];
		}

		private static int[] DetermineBufferOffsets(IList<BufferBlock> blocks)
		{
			int[] result = new int[blocks.Count];
			int[] bufferEnds = new int[blocks.Count];

			foreach((int index, BufferBlock block) in blocks.OrderByDescending(x => x.width).Index())
			{
				int yEnd = block.yOffset + block.height;
				List<(int start, int end)> blockedRanges = [];

				for(int i = 0; i < index; i++)
				{
					BufferBlock other = blocks[i];
					int otherYEnd = other.height + other.yOffset;

					if(yEnd <= other.yOffset || block.yOffset >= otherYEnd)
					{
						// cannot overlap
						continue;
					}

					int otherBufferOffset = result[other.index];
					blockedRanges.Add((otherBufferOffset, otherBufferOffset + other.width));
				}

				int offset = 0;

				if(blockedRanges.Count > 0)
				{
					blockedRanges.Sort((a, b) => a.start.CompareTo(b.start));

					if(blockedRanges[0].start < block.width)
					{
						for(int i = 1; i < blockedRanges.Count; i++)
						{
							if(blockedRanges[i].start - blockedRanges[i - 1].end >= offset)
							{
								offset = blockedRanges[i - 1].end;
								break;
							}
						}

						if(offset == 0)
						{
							offset = blockedRanges[^1].end;
						}
					}
				}

				result[block.index] = offset;
				bufferEnds[block.index] = offset + block.width;
			}

			if(bufferEnds.Any(x => x > ushort.MaxValue))
			{
				throw new InvalidOperationException("Mesh data does not fit into the cache!");
			}

			return result;
		}

		private static void UpdateChunkVertexOffsets(IEnumerable<(int bufferIndex, VertexChunk[])> vertexChunks, IEnumerable<(int bufferIndex, PolyChunk[])> polyChunks, int[] bufferOffsets)
		{
			foreach((int bufferIndex, VertexChunk[] chunks) in vertexChunks)
			{
				ushort bufferOffset = (ushort)bufferOffsets[bufferIndex];
				foreach(VertexChunk chunk in chunks)
				{
					chunk.IndexOffset += bufferOffset;
				}
			}

			foreach((int bufferIndex, PolyChunk[] chunks) in polyChunks)
			{
				ushort bufferOffset = (ushort)bufferOffsets[bufferIndex];
				foreach(PolyChunk chunk in chunks)
				{
					if(chunk is VolumeChunk volumeChunk)
					{
						for(int i = 0; i < volumeChunk.Polygons.Length; i++)
						{
							IChunkVolumePolygon polygon = volumeChunk.Polygons[i];

							for(int j = 0; j < polygon.NumIndices; j++)
							{
								polygon[j] += bufferOffset;
							}

							volumeChunk.Polygons[i] = polygon;
						}
					}
					else if(chunk is StripChunk stripChunk)
					{
						foreach(ChunkStrip strip in stripChunk.Strips)
						{
							for(int i = 0; i < strip.Corners.Length; i++)
							{
								strip.Corners[i].Index += bufferOffset;
							}
						}
					}
				}
			}
		}
	}
}
