using SA3D.Modeling.Convert.Weighted;
using SA3D.Modeling.Mesh;
using SA3D.Modeling.Mesh.Chunk;
using SA3D.Modeling.Mesh.Chunk.PolyChunks;
using SA3D.Modeling.Mesh.Chunk.Structs;
using SA3D.Modeling.ObjectData;
using SA3D.Modeling.ObjectData.Structs;
using SA3D.Modeling.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SA3D.Modeling.Convert.MeshConverters
{
	internal class ChunkMeshConverter : MeshConverterBase
	{
		private bool _useVersion2Weights;

		private struct BufferVertex
		{
			public Vector3 position;
			public Vector3 normal;
			public Color diffuse;
			public Color specular;
			public uint userAttribute;
			public List<(int nodeIndex, float weight)>? weights;
		}

		private readonly BufferVertex[] _vertexBuffer = new BufferVertex[ushort.MaxValue];
		private readonly Dictionary<byte, PolyChunk[]> _polyChunkCache = [];

		private VertexChunkType _activeVertexType;
		private WeightedMaterial _activeMaterial;
		private bool _hasColors;

		private readonly List<WeightedCorner[]> _triangleSets = [];
		private readonly List<WeightedMaterial> _materials = [];

		private readonly List<WeightedCorner> _volumeTriangles = [];


		private void ProcessVertexChunk(int nodeIndex, Matrix4x4 worldMatrix, VertexChunk chunk)
		{
			bool hasNormals = chunk.Type.CheckHasNormal();
			bool hasDiffuse = chunk.Type.CheckHasDiffuseColor();
			bool hasSpecular = chunk.Type.CheckHasSpecularColor();
			bool hasWeights = chunk.Type.CheckHasAttributes();
			bool hasUserAttributes = chunk.Type.CheckHasUserAttributes();
			_activeVertexType = chunk.Type;

			for(int i = 0; i < chunk.Vertices.Length; i++)
			{
				ChunkVertex chunkVertex = chunk.Vertices[i];

				ushort index = (ushort)(chunk.IndexOffset + (hasWeights ? chunkVertex.Index : i));

				BufferVertex vertex = !hasWeights || chunk.WeightMode == WeightMode.Start
					? new() { weights = [] }
					: _vertexBuffer[index];

				float weight = 1;
				if(hasWeights)
				{
					weight = _useVersion2Weights ? chunkVertex.Weight2 : chunkVertex.Weight;
				}

				vertex.weights!.Add((nodeIndex, weight));

				vertex.position += Vector3.Transform(chunkVertex.Position, worldMatrix) * weight;

				if(hasNormals)
				{
					vertex.normal += Vector3.TransformNormal(chunkVertex.Normal, worldMatrix) * weight;
				}

				if(hasDiffuse)
				{
					vertex.diffuse = chunkVertex.Diffuse;
				}

				if(hasSpecular)
				{
					vertex.specular = chunkVertex.Specular;
				}

				if(hasUserAttributes)
				{
					vertex.userAttribute = chunkVertex.Attributes;
				}

				_vertexBuffer[index] = vertex;
			}
		}

		private void ProcessStripChunk(StripChunk chunk)
		{
			const float lduvFactor = 1f / 256f;
			const float hduvFactor = 1f / 1024f;

			bool hasPolyColors = chunk.Type.CheckStripHasColors();
			bool hasTexCoords = chunk.Type.GetStripTexCoordCount() > 0;
			float uvFactor = chunk.Type.CheckStripHasHDTexcoords() ? hduvFactor : lduvFactor;

			bool vertexColors = _activeVertexType.CheckHasDiffuseColor();

			_hasColors |= vertexColors | hasPolyColors;

			List<WeightedCorner> corners = [];

			foreach(ChunkStrip strip in chunk.Strips)
			{
				WeightedCorner[] rawCorners = new WeightedCorner[strip.Corners.Length];

				for(int i = 0; i < rawCorners.Length; i++)
				{
					ChunkCorner chunkCorner = strip.Corners[i];
					WeightedCorner corner = new(chunkCorner.Index);

					if(hasPolyColors)
					{
						corner.Diffuse = chunkCorner.Color;
					}
					else if(vertexColors)
					{
						corner.Diffuse = _vertexBuffer[chunkCorner.Index].diffuse;
					}

					if(hasTexCoords)
					{
						corner.TexCoord = chunkCorner.Texcoord * uvFactor;
					}

					rawCorners[i] = corner;
				}

				corners.AddRange(PolygonUtil.UnravelStrip(rawCorners, strip.Reversed));
			}

			if(corners.Count > 0)
			{
				_triangleSets.Add([.. corners]);
				_materials.Add(_activeMaterial);
			}
		}

		private void ProcessVolumeChunk(VolumeChunk chunk)
		{
			WeightedCorner[] rawCorners = new WeightedCorner[chunk.Polygons.Sum(x => x.NumIndices)];
			int index = 0;
			foreach(IChunkVolumePolygon polygon in chunk.Polygons)
			{
				for(int j = 0; j < polygon.NumIndices; j++, index++)
				{
					rawCorners[index] = new(polygon[j]);
				}
			}

			switch(chunk.Type)
			{
				case PolyChunkType.Volume_Triangle:
					_volumeTriangles.AddRange(rawCorners);
					break;
				case PolyChunkType.Volume_Quad:
					_volumeTriangles.AddRange(PolygonUtil.UnravelQuads(rawCorners));
					break;
				case PolyChunkType.Volume_Strip:
					_volumeTriangles.AddRange(PolygonUtil.UnravelStrips(rawCorners, chunk.Polygons.Cast<ChunkVolumeStrip>().Select(x => (x.NumIndices, x.Reversed))));
					break;
			}
		}


		private MeshConversionInfo CompileMesh(IEnumerable<WeightedCorner[]> triangleSets, IEnumerable<WeightedMaterial> materials)
		{
			SortedSet<ushort> usedVertexIndices = [.. triangleSets.SelectMany(x => x).Select(x => x.VertexIndex)];

			SortedSet<int> nodeIndices = [];
			foreach(ushort vertexIndex in usedVertexIndices)
			{
				List<(int nodeIndex, float weight)> weights = _vertexBuffer[vertexIndex].weights!;
				if(weights.Count == 1)
				{
					nodeIndices.Add(weights[0].nodeIndex);
				}
				else
				{
					nodeIndices.UnionWith(weights.Where(x => x.weight > 0).Select(x => x.nodeIndex));
				}
			}

			int attachedNode = GetLeastCommonAncestor(nodeIndices);
			nodeIndices.Add(attachedNode);

			int maxNodeIndex = nodeIndices.Max();
			int[] weightNodeMapping = new int[maxNodeIndex + 1];
			int[] weightNodes = [.. nodeIndices];

			for(int i = 0; i < weightNodes.Length; i++)
			{
				weightNodeMapping[weightNodes[i]] = i;
			}

			Matrix4x4 inverseWorldSpaceMatrix = NodeWorldMatrices[attachedNode].worldMatrix;
			Matrix4x4.Invert(inverseWorldSpaceMatrix, out inverseWorldSpaceMatrix);

			WeightedVertex[] vertices = new WeightedVertex[usedVertexIndices.Count];
			Dictionary<ushort, ushort> vertexIndexMapping = [];
			bool hasNormals = _activeVertexType.CheckHasNormal();
			bool hasWeights = weightNodes.Length > 1;

			foreach(ushort vertexIndex in usedVertexIndices)
			{
				BufferVertex bufferVertex = _vertexBuffer[vertexIndex];

				Vector3 position = Vector3.Transform(bufferVertex.position, inverseWorldSpaceMatrix);
				Vector3 normal = WeightedVertex.DefaultNormal;

				if(hasNormals)
				{
					normal = Vector3.TransformNormal(bufferVertex.normal, inverseWorldSpaceMatrix);
				}

				WeightedVertex vertex;

				if(!hasWeights)
				{
					vertex = new(position, normal);
				}
				else
				{
					vertex = new(position, normal, weightNodes.Length);

					if(bufferVertex.weights!.Count == 1)
					{
						vertex.Weights![weightNodeMapping[bufferVertex.weights[0].nodeIndex]] = 1;
					}
					else
					{
						float sum = 0;
						foreach((int nodeIndex, float weight) in bufferVertex.weights!)
						{
							if(weight > 0)
							{
								vertex.Weights![weightNodeMapping[nodeIndex]] = weight;
								sum += weight;
							}
						}

						float normalizeFactor = 1f / sum;

						for(int j = 0; j < vertex.Weights!.Length; j++)
						{
							vertex.Weights![j] *= normalizeFactor;
						}
					}
				}

				ushort index = (ushort)vertexIndexMapping.Count;
				vertexIndexMapping[vertexIndex] = index;
				vertices[index] = vertex;
			}

			MeshConversionInfo result = new(new(
				vertices,
				[.. triangleSets],
				[.. materials]
			))
			{
				HasNormals = hasNormals,
				HasDiffuseColors = _hasColors,
				ChunkVertexType = _activeVertexType
			};

			if(hasWeights)
			{
				result.Mesh.WeightNodeIndexMap = [.. weightNodes.Where(x => x != attachedNode).Select(x => x - attachedNode)];
			}

			result.AttachedNodeIndices.Add(attachedNode);

			foreach(WeightedCorner[] triangleSet in result.Mesh.TriangleSets)
			{
				for(int i = 0; i < triangleSet.Length; i++)
				{
					triangleSet[i].VertexIndex = vertexIndexMapping[triangleSet[i].VertexIndex];
				}
			}

			return result;
		}

		private MeshConversionInfo CompileVisualMesh()
		{
			return CompileMesh(_triangleSets, _materials);
		}

		private MeshConversionInfo CompileVolumeMesh()
		{
			MeshConversionInfo result = CompileMesh(
				[[.. _volumeTriangles]], 
				[WeightedMaterial.DefaultValues]
			);

			result.VolumeMesh = true;

			return result;
		}


		private (MeshConversionInfo? visual, MeshConversionInfo? volume) ProcessPolygonChunks(string? meshname, IEnumerable<PolyChunk> chunks)
		{
			byte cacheIndex = 0;
			List<PolyChunk>? cache = null;

			foreach(PolyChunk chunk in chunks)
			{
				if(cache != null)
				{
					cache.Add(chunk);
					continue;
				}

				switch(chunk)
				{
					case BlendAlphaChunk blendAlphaChunk:
						_activeMaterial.SourceBlendMode = blendAlphaChunk.SourceBlendMode;
						_activeMaterial.DestinationBlendmode = blendAlphaChunk.DestinationBlendMode;
						break;

					case MipmapDistanceMultiplierChunk mipmapDistanceMultiplierChunk:
						_activeMaterial.MipmapDistanceMultiplier = mipmapDistanceMultiplierChunk.MipmapDistanceMultiplier;
						break;

					case SpecularExponentChunk specularExponentChunk:
						_activeMaterial.SpecularExponent = specularExponentChunk.SpecularExponent;
						break;

					case CacheListChunk cacheListChunk:
						cacheIndex = cacheListChunk.List;
						cache = [];
						break;

					case DrawListChunk drawListChunk:
						ProcessPolygonChunks(null, _polyChunkCache[drawListChunk.List]);
						break;

					case TextureChunk textureChunk:
						if(textureChunk.Second)
						{
							continue;
						}

						_activeMaterial.ClampV = textureChunk.ClampV;
						_activeMaterial.ClampU = textureChunk.ClampU;
						_activeMaterial.MirrorV = textureChunk.MirrorV;
						_activeMaterial.MirrorU = textureChunk.MirrorU;
						_activeMaterial.TextureIndex = textureChunk.TextureID;
						_activeMaterial.AnisotropicFiltering = textureChunk.SuperSample;
						_activeMaterial.TextureFiltering = textureChunk.FilterMode;
						break;

					case MaterialChunk materialChunk:
						if(materialChunk.Second)
						{
							continue;
						}

						if(materialChunk.Diffuse.HasValue)
						{
							_activeMaterial.Diffuse = materialChunk.Diffuse.Value;
						}

						if(materialChunk.Ambient.HasValue)
						{
							_activeMaterial.Ambient = materialChunk.Ambient.Value;
						}

						if(materialChunk.Specular.HasValue)
						{
							_activeMaterial.Specular = materialChunk.Specular.Value;
							_activeMaterial.SpecularExponent = materialChunk.SpecularExponent;
						}

						break;

					case VolumeChunk volumeChunk:
						ProcessVolumeChunk(volumeChunk);
						break;

					case StripChunk stripChunk:
						if(_activeMaterial.TextureFiltering == FilterMode.TrilinearB)
						{
							// we ignore the second part of trilinear meshes, since those should be identical to the first part (TrinilearA)
							continue;
						}

						_activeMaterial.NoLighting = stripChunk.IgnoreLight;
						_activeMaterial.NoSpecular = stripChunk.IgnoreSpecular;
						_activeMaterial.NoAmbient = stripChunk.IgnoreAmbient;
						_activeMaterial.BackfaceCulling = !stripChunk.DoubleSide;
						_activeMaterial.Flat = stripChunk.FlatShading;
						_activeMaterial.NormalMapping = stripChunk.EnvironmentMapping;
						_activeMaterial.AlphaMode = stripChunk.AlphaMode;
						_activeMaterial.TexcoordPrecisionLevel = (byte)(stripChunk.Type.CheckStripHasHDTexcoords() ? 2 : 0);

						ProcessStripChunk(stripChunk);
						break;
				}
			}

			if(cache != null)
			{
				_polyChunkCache[cacheIndex] = [.. cache];
			}

			if(meshname == null)
			{
				return (null, null);
			}

			MeshConversionInfo? visual = null;
			if(_triangleSets.Count > 0)
			{
				visual = CompileVisualMesh();
				visual.Mesh.Label = meshname;
			}

			MeshConversionInfo? volume = null;
			if(_volumeTriangles.Count > 0)
			{
				volume = CompileVolumeMesh();
				volume.Mesh.Label = meshname;
			}

			if(visual != null && volume != null)
			{
				visual.Mesh.Label += "_vis";
				volume.Mesh.Label += "_vol";
			}

			_triangleSets.Clear();
			_materials.Clear();
			_hasColors = false;
			_volumeTriangles.Clear();

			return (visual, volume);
		}

		protected override MeshConversionInfo[] ConvertToWeighted()
		{
			List<MeshConversionInfo> result = [];
			Array.Clear(_vertexBuffer);

			_activeVertexType = default;
			_activeMaterial = WeightedMaterial.DefaultValues;
			_polyChunkCache.Clear();

			// Little "hack" to get this without having to reimplement the query
			_useVersion2Weights = ModelAsciiIOContext.FromModel(Format.Chunk, RootNode, default).UseVersion2Weights;

			for(int i = 0; i < NodeWorldMatrices.Length; i++)
			{
				(Node node, Matrix4x4 worldMatrix) = NodeWorldMatrices[i];

				if(node.MeshData is not ChunkMesh mesh)
				{
					continue;
				}

				if(mesh.VertexChunks != null)
				{
					foreach(VertexChunk vertexChunk in mesh.VertexChunks)
					{
						ProcessVertexChunk(i, worldMatrix, vertexChunk);
					}
				}


				if(mesh.PolyChunks != null)
				{
					(MeshConversionInfo? visual, MeshConversionInfo? volume) = ProcessPolygonChunks(mesh.Label, mesh.PolyChunks);

					if(visual != null)
					{
						result.Add(visual);
					}

					if(volume != null)
					{
						result.Add(volume);
					}
				}
			}

			return [.. result];
		}
	}
}
