﻿using SLZ.Marrow.Pool;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL.Interfaces;

internal interface ISavedObject<TImplementor, TSavedObject, TContext> : IEquatable<TImplementor>, ISerializableStruct<TImplementor> 
    where TImplementor : struct, ISavedObject<TImplementor, TSavedObject, TContext>
{
    // use blocking calls for Construct because it accesses unity shit
    public void Construct(TContext context, TSavedObject prepareToSerialize);
    public Task<TSavedObject> Initialize();
}