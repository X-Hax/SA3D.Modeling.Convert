using SA3D.Modeling.Convert.MeshConverters;
using SA3D.Modeling.Convert.WeightedMeshConverters;
using SA3D.Modeling.Mesh;
using SA3D.Modeling.ObjectData;
using System;
using System.Collections.Generic;

namespace SA3D.Modeling.Convert
{
	/// <summary>
	/// Set of model data to convert
	/// </summary>
	public class MeshConversionSet
	{
		/// <summary>
		/// Meshes to convert
		/// </summary>
		public List<MeshConversionInfo> MeshInfo { get; set; } = [];

		/// <summary>
		/// Creates a new, empty mesh conversion set
		/// </summary>
		public MeshConversionSet() { }

		/// <summary>
		/// Creates a mesh conversion set from existing meshes
		/// </summary>
		/// <param name="meshes"></param>
		public MeshConversionSet(IEnumerable<MeshConversionInfo> meshes)
		{
			MeshInfo = [.. meshes];
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="model"></param>
		/// <returns></returns>
		/// <exception cref="NotImplementedException"></exception>
		/// <exception cref="InvalidOperationException"></exception>
		public static MeshConversionSet ConvertToWeighted(Node model)
		{
			MeshConversionSet result = new();

			if(model.GetMeshFormat() is not MeshFormat format)
			{
				return result;
			}

			MeshConverterBase converter = format switch
			{
				MeshFormat.Basic => new BasicMeshConverter(),
				MeshFormat.Chunk => new ChunkMeshConverter(),
				MeshFormat.Ginja => new GinjaMeshConverter(),
				_ => throw new InvalidOperationException(),
			};


			result.MeshInfo.AddRange(converter.ConvertToWeighted(model));

			// Removing degenerate polygons and meshes
			_ = result.MeshInfo.RemoveAll(x => x.Mesh.EnsurePolygonsValid() || x.Mesh.TriangleSets.Length == 0);

			// TODO merge the meshes that share nodes(?)

			return result;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="model"></param>
		/// <param name="format"></param>
		/// <param name="parameters"></param>
		/// <exception cref="InvalidOperationException"></exception>
		public void ConvertWeighted(Node model, MeshFormat format, WeightedMeshConverterParameters parameters)
		{
			WeightedMeshConverterBase converter = format switch
			{
				MeshFormat.Basic => new BasicWeightedMeshConverter(),
				MeshFormat.Chunk => new ChunkWeightedMeshConverter(),
				MeshFormat.Ginja => new GinjaWeightedMeshConverter(),
				_ => throw new InvalidOperationException(),
			};

			converter.ConvertWeighted(model, MeshInfo, parameters);
		}
	}
}
