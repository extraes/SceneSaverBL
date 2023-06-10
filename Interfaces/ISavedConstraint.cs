using SLZ.Marrow.Pool;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SceneSaverBL.Interfaces;

internal interface ISavedConstraint<TSavedConstraint> : IEquatable<TSavedConstraint>, ISerializableStruct<TSavedConstraint> where TSavedConstraint : struct, ISavedConstraint<TSavedConstraint>
{
    public (int, int) DependentOn { get; }

    public void Construct(AssetPoolee[] poolees, ConstraintTracker constraint);
    public void Initialize(AssetPoolee[] poolees, Constrainer constrainer);
}
