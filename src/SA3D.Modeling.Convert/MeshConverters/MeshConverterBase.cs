using SA3D.Modeling.ObjectData;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SA3D.Modeling.Convert.MeshConverters
{
	internal abstract class MeshConverterBase
	{
		protected Node RootNode { get; private set; }

		protected (Node node, Matrix4x4 worldMatrix)[] NodeWorldMatrices { get; private set; }

		protected Dictionary<Node, int> NodeMap { get; private set; }


		protected MeshConverterBase()
		{
			RootNode = new();
			NodeWorldMatrices = [];
			NodeMap = [];
		}

		public MeshConversionInfo[] ConvertToWeighted(Node model)
		{
			RootNode = model;
			NodeWorldMatrices = model.GetWorldMatrixTree();
			NodeMap = model.GetTreeNodeEnumerable().Select((x, i) => (x, i)).ToDictionary();
			return ConvertToWeighted();
		}

		private static Node? GetLeastCommonAncestor(Node node, HashSet<Node> descendants, out int count)
		{
			count = descendants.Contains(node) ? 1 : 0;

			foreach(Node child in node)
			{
				if(GetLeastCommonAncestor(child, descendants, out int childCount) is Node result)
				{
					count = childCount;
					return result;
				}

				count += childCount;
			}

			if(count == descendants.Count)
			{
				return node;
			}

			return null;
		}

		protected Node GetLeastCommonAncestor(HashSet<Node> descendants)
		{
			if(descendants.Count == 1)
			{
				return descendants.First();
			}
			else if(descendants.Count == 0)
			{
				return RootNode;
			}

			return GetLeastCommonAncestor(RootNode, descendants, out int _) ?? RootNode;
		}

		protected int GetLeastCommonAncestor(IEnumerable<int> descendants)
		{
			return NodeMap[GetLeastCommonAncestor([.. descendants.Select(x => NodeWorldMatrices[x].node)])];
		}

		protected abstract MeshConversionInfo[] ConvertToWeighted();
	}
}
