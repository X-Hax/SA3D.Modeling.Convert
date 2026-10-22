namespace SA3D.Modeling.Convert
{
	/// <summary>
	/// Converter parameters
	/// </summary>
	public struct WeightedMeshConverterParameters
	{
		/// <summary>
		/// Whether to split weighted meshes into per-node segments if the target format does not support weights
		/// </summary>
		public bool SplitWeighted { get; set; }

		/// <summary>
		/// Whether to use weldings when converting a weighted mesh to <see cref="Structs.Format.Basic"/>
		/// </summary>
		public bool UseMetaWeights { get; set; }

		/// <summary>
		/// Use 16 bit weights instead of 8 bit weights when exporting to chunk
		/// </summary>
		public bool HighPrecisionWeights { get; set; }

		/// <summary>
		/// Merge vertices that are closer together than <see cref="VertexMergeDistance"/> units
		/// </summary>
		public bool MergeVertices { get; set; }

		/// <summary>
		/// Maximum distance allowed between two vertices before merging them (if <see cref="MergeVertices"/> is enabled)
		/// </summary>
		public float VertexMergeDistance { get; set; }


		/// <summary>
		/// The default constructor
		/// </summary>
		public WeightedMeshConverterParameters()
		{
			VertexMergeDistance = 0.001f;
		}
	}
}
