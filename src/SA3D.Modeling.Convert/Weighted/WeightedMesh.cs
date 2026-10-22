using SA3D.Common;
using System;
using System.Collections.Generic;

namespace SA3D.Modeling.Convert.Weighted
{
	/// <summary>
	/// Helper class for model conversions
	/// </summary>
	public class WeightedMesh : ICloneable
	{
		/// <summary>
		/// Label of the weighted mesh, if available.
		/// </summary>
		public string? Label { get; set; }

		/// <summary>
		/// Vertex data.
		/// </summary>
		public WeightedVertex[] Vertices { get; set; }

		/// <summary>
		/// Index mapping to nodes that this mesh is influenced by (relative to the "root" or "attached" node). <br/>
		/// Weighted vertices need to have a weights array of either <see langword="null"/> or a length of <see cref="WeightNodeIndexMap"/>.length + 1, where the index at 0 is always the base node
		/// </summary>
		public int[]? WeightNodeIndexMap { get; set; }

		/// <summary>
		/// Triangle sets.
		/// </summary>
		public WeightedCorner[][] TriangleSets { get; set; }

		/// <summary>
		/// Materials for each triangle set.
		/// </summary>
		public WeightedMaterial[] Materials { get; set; }


		/// <summary>
		/// Initialize an instance of the <see cref="WeightedMesh"/> class
		/// </summary>
		/// <param name="vertices"></param>
		/// <param name="triangleSets"></param>
		/// <param name="materials"></param>
		public WeightedMesh(
			WeightedVertex[] vertices,
			WeightedCorner[][] triangleSets,
			WeightedMaterial[] materials)
		{
			Vertices = vertices;
			TriangleSets = triangleSets;
			Materials = materials;
		}

		/// <summary>
		/// Initialize an instance of the <see cref="WeightedMesh"/> class
		/// </summary>
		/// <param name="vertices"></param>
		/// <param name="triangleSets"></param>
		/// <param name="materials"></param>
		/// <param name="label"></param>
		public WeightedMesh(
			WeightedVertex[] vertices,
			WeightedCorner[][] triangleSets,
			WeightedMaterial[] materials,
			string? label)
		{
			Vertices = vertices;
			TriangleSets = triangleSets;
			Materials = materials;
			Label = label;
		}

		#region Utilities

		/// <summary>
		/// Checks for degenerate triangles and removes them.
		/// </summary>
		/// <returns>Whether the model contained degenerate triangles that have been removed.</returns>
		public bool EnsurePolygonsValid()
		{
			List<WeightedCorner[]> newTriangleSets = [];
			List<WeightedMaterial> newMaterials = [];
			bool changed = false;

			for(int i = 0; i < TriangleSets.Length; i++)
			{
				List<int> degenerates = [];

				WeightedCorner[] triangles = TriangleSets[i];
				for(int j = 0; j < triangles.Length; j += 3)
				{
					int index1 = triangles[j].VertexIndex;
					int index2 = triangles[j + 1].VertexIndex;
					int index3 = triangles[j + 2].VertexIndex;

					if(index1 == index2
						|| index2 == index3
						|| index3 == index1)
					{
						degenerates.Add(j);
					}
				}

				changed |= degenerates.Count > 0;

				if(degenerates.Count == triangles.Length / 3)
				{
					continue;
				}

				newMaterials.Add(Materials[i]);

				if(degenerates.Count == 0)
				{
					newTriangleSets.Add(triangles);
				}
				else
				{
					List<WeightedCorner> newTriangles = [];
					for(int j = 0; j < triangles.Length; j += 3)
					{
						if(degenerates.Count > 0 && degenerates[0] == j)
						{
							degenerates.RemoveAt(0);
							continue;
						}

						newTriangles.Add(triangles[j]);
						newTriangles.Add(triangles[j + 1]);
						newTriangles.Add(triangles[j + 2]);
					}

					newTriangleSets.Add([.. newTriangles]);
				}
			}

			if(changed)
			{
				TriangleSets = [.. newTriangleSets];
				Materials = [.. newMaterials];
			}

			return changed;
		}


		object ICloneable.Clone()
		{
			return Clone();
		}

		/// <summary>
		/// Creates a deep clone of the mesh.
		/// </summary>
		/// <returns>The cloned mesh.</returns>
		public WeightedMesh Clone()
		{
			return new(
				Vertices.ContentClone(),
				TriangleSets.ContentClone(),
				[.. Materials]
			)
			{
				Label = Label
			};
		}

		#endregion

		/// <inheritdoc/>
		public override string ToString()
		{
			return Label ?? "NULL";
		}
	}
}
