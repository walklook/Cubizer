﻿using System.Collections;
using System.Collections.Generic;

namespace Cubizer
{
	public interface IVoxelModel
	{
		int CalcFaceCountAsAllocate(ref Dictionary<string, int> entities);

		IEnumerable GetEnumerator();
	}
}
