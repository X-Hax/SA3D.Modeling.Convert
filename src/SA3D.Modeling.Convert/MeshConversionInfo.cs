
using SA3D.Modeling.Convert.Weighted;
using SA3D.Modeling.Mesh.Chunk;

namespace SA3D.Modeling.Convert
{
	/// <summary>
	/// Mesh conversion options
	/// </summary>
	public class MeshConversionInfo
	{
		/// <summary>
		/// Mesh to convert
		/// </summary>
		public WeightedMesh Mesh { get; set; }

		/// <summary>
		/// Index to the node at which the mesh is attached
		/// </summary>
		public int AttachedNodeIndex { get; set; }


		/// <summary>
		/// Whether the mesh has normals (even if all normals point in the same direction).
		/// </summary>
		public bool HasNormals { get; set; }

		/// <summary>
		/// Whether vertices have user attributes
		/// </summary>
		public bool HasVertexUserAttributes { get; set; }

		/// <summary>
		/// Whether the mesh has diffuse colors (even if they are all white).
		/// </summary>
		public bool HasDiffuseColors { get; set; }

		/// <summary>
		/// Whether the mesh has specular colors (even if they are all black).
		/// </summary>
		public bool HasSpecularColors { get; set; }

		/// <summary>
		/// Whether the mesh is a (chunk-) volume mesh
		/// </summary>
		public bool VolumeMesh { get; set; }

		/// <summary>
		/// Chunk vertex type converted from / to convert to
		/// </summary>
		public VertexChunkType ChunkVertexType { get; set; }

		/// <summary>
		/// Resulting mesh data will have no bounds calculated.
		/// </summary>
		public bool NoBounds { get; set; }


		/// <summary>
		/// Initializes a new instance of the <see cref="MeshConversionInfo"/> class with a mesh
		/// </summary>
		/// <param name="mesh"></param>
		public MeshConversionInfo(WeightedMesh mesh)
		{
			Mesh = mesh;
		}

		/// <inheritdoc/>
		public override string ToString()
		{
			return Mesh.ToString();
		}
	}
}
