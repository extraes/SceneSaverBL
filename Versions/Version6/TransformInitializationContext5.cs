using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL.Versions.Version6;

internal struct TransformInitializationContext6
{
    public Transform transform;

    public TransformInitializationContext6(Transform t)
    {
        this.transform = t;
    }
}
