using SA3D.Modeling.Convert.Weighted;
using System.Collections.Generic;

namespace SA3D.Modeling.Convert.MeshConverters
{
	internal class PolygonUtil
	{
		public static IEnumerable<WeightedCorner> UnravelQuads(IEnumerable<WeightedCorner> rawCorners)
		{
			IEnumerator<WeightedCorner> enumerator = rawCorners.GetEnumerator();

			while(enumerator.MoveNext())
			{
				WeightedCorner c1 = enumerator.Current;

				_ = enumerator.MoveNext();
				WeightedCorner c2 = enumerator.Current;

				_ = enumerator.MoveNext();
				WeightedCorner c3 = enumerator.Current;

				_ = enumerator.MoveNext();
				WeightedCorner c4 = enumerator.Current;

				yield return c1;
				yield return c2;
				yield return c3;

				yield return c4;
				yield return c3;
				yield return c2;
			}
		}

		public static IEnumerable<WeightedCorner> UnravelStrip(IEnumerable<WeightedCorner> rawCorners, bool reverse)
		{
			IEnumerator<WeightedCorner> enumerator = rawCorners.GetEnumerator();

			_ = enumerator.MoveNext();
			WeightedCorner c1 = enumerator.Current;

			_ = enumerator.MoveNext();
			WeightedCorner c2 = enumerator.Current;

			while(enumerator.MoveNext())
			{
				WeightedCorner c3 = enumerator.Current;

				if(c1.VertexIndex != c2.VertexIndex
					&& c2.VertexIndex != c3.VertexIndex
					&& c3.VertexIndex != c1.VertexIndex)
				{
					if(reverse)
					{
						// I would've actually done 3, 2, 1 to match the "reverse" name,
						// per-polygon information is stored on the current corner, so we
						// must keep the current corner at the end of the polygon for those

						yield return c2;
						yield return c1;
						yield return c3;
					}
					else
					{
						yield return c1;
						yield return c2;
						yield return c3;
					}
				}

				c1 = c2;
				c2 = c3;
				reverse = !reverse;
			}
		}

		public static IEnumerable<WeightedCorner> UnravelStrips(IEnumerable<WeightedCorner> rawCorners, IEnumerable<(int numIndices, bool reverse)> strips)
		{
			IEnumerator<WeightedCorner> enumerator = rawCorners.GetEnumerator();

			foreach((int numIndices, bool reverseStart) in strips)
			{
				bool reverse = reverseStart;

				_ = enumerator.MoveNext();
				WeightedCorner c1 = enumerator.Current;

				_ = enumerator.MoveNext();
				WeightedCorner c2 = enumerator.Current;

				for(int i = 2; i < numIndices; i++)
				{
					_ = enumerator.MoveNext();
					WeightedCorner c3 = enumerator.Current;

					if(c1.VertexIndex != c2.VertexIndex
						&& c2.VertexIndex != c3.VertexIndex
						&& c3.VertexIndex != c1.VertexIndex)
					{
						if(reverse)
						{
							// I would've actually done 3, 2, 1 to match the "reverse" name,
							// per-polygon information is stored on the current corner, so we
							// must keep the current corner at the end of the polygon for those

							yield return c2;
							yield return c1;
							yield return c3;
						}
						else
						{
							yield return c1;
							yield return c2;
							yield return c3;
						}
					}

					c1 = c2;
					c2 = c3;
					reverse = !reverse;
				}
			}
		}
	}
}
