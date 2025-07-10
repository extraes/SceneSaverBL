using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow.Warehouse;
using Jevil.Patching;
using System.Diagnostics;

namespace SceneSaverBL;

internal static class SpawnerStates
{
    public struct State
    {
        public object spawnedThing;
        public int relativeOrder;
        public Vector3 pointStart;
        public Vector3 pointEnd;

        // mrw hard-casts 😃
        public readonly ConstraintTracker AsConstraint => (ConstraintTracker)spawnedThing;
        public readonly ObjectDestructible AsObjDest => (ObjectDestructible)spawnedThing;
    }

    public static void Init()
    {
        Board.Init();
        Constraint.Init();
#if DEBUG
        SceneSaverBL.Log("Spawner states will now be cached!");
#endif
    }

    public static class Board
    {
        static State? recentState;
        static Queue<GameObject> spawnedPlanks = new();
        static Queue<UniTaskCompletionSource<GameObject>> waitingForPlanks = new();
        // this WILL leak memory... maybe?... theres no OnDestroy to hook? idfk its pooled it should be fine
        static Dictionary<int, State> states = new();
        static int counter;
        
        static Poolee? lastSpawnedBoard;
        internal static void Init()
        {
            Hook.OntoMethod(typeof(Poolee), nameof(Poolee.OnEnable), static (Poolee inst) =>
            {
                if (inst?.SpawnableCrate?.Barcode?.ID is null)
                    return;

                if (!SaveUtils.PlankBarcodesSet.Contains(inst.SpawnableCrate.Barcode.ID))
                    return;

                lastSpawnedBoard = inst;
            });

            Hook.OntoMethod(typeof(BoardGenerator._BoardSpawnerAsync_d__29), nameof(BoardGenerator._BoardSpawnerAsync_d__29.MoveNext), (BoardGenerator._BoardSpawnerAsync_d__29 instance) =>
            {
                if (instance.__1__state == -2)
                {
                    GetNewObjDest(instance.__4__this);
                }
            });
            //Hook.OntoMethod(typeof(BoardGenerator).GetMethod(nameof(BoardGenerator._BoardSpawnerAsync_d__29)), BoardSpawned);
        }

        static void GetNewObjDest(BoardGenerator boardGun)
        {
            //EnsurePool(); not necessary, boardgun initializes planks for us
#if DEBUG
            SceneSaverBL.Log("Board spawned from boardspawner");
#endif

            if (lastSpawnedBoard == null)
            {
                SceneSaverBL.Warn("Was told that a board was spawned, but no poolee with the barcode called OnEnable! Ignoring!");
                return;
            }

            ObjectDestructible? objDest = Instances<ObjectDestructible>.Get(lastSpawnedBoard.gameObject);

            if (objDest == null)
            {
                SceneSaverBL.Warn("Was told that a board was spawned but there was no ObjectDestructible found on it! Try telling dev to recheck the hierarchy?");
                return;
            }
            
            // assuming the last thing in the list is the most recently spawned thing 😎
            recentState = new()
            {
                relativeOrder = counter++,
                spawnedThing = objDest,
                pointStart = boardGun.firstPoint,
                pointEnd = boardGun.EndPoint,
            };

            BoardSpawned(lastSpawnedBoard.gameObject);


        }

        //static void EnsurePool()
        //{
        //    bool anyNull = false;
        //    for (int i = 0; i < plankPools.Count; i++)
        //    {
        //        Pool? pool = plankPools[i];
        //        if (pool is null || pool.WasCollected)
        //        {
        //            anyNull = true;
        //            break;
        //        }
        //    }

        //    if (!anyNull)
        //        return;

        //    foreach (var pool in AssetSpawner._instance._poolList)
        //    {
        //        if (SaveUtils.IsNewPlank(pool._crate.Barcode.ID).HasValue)
        //            plankPools.Add(pool);
        //    }
        //}

        static void BoardSpawned(GameObject spawnedBoard)
        {
#if DEBUG
            SceneSaverBL.Log("Board spawned successfully - spawncallback called for " + spawnedBoard.name);
#endif

            if (recentState.HasValue)
            {
                ObjectDestructible? objDest = Instances<ObjectDestructible>.Get(spawnedBoard);

#if DEBUG
                if (objDest == null)
                {
                    SceneSaverBL.Warn("ObjDest was null on current spawned object! Trying to sidestep now!");
                    objDest = spawnedBoard.GetComponentInChildren<ObjectDestructible>();
                    if (objDest == null)
                        SceneSaverBL.Log($"Recovery was successful! Found ObjDest located at " + objDest.transform.GetFullPath());
                    else
                        SceneSaverBL.Warn($"Recovery wasn't successful! Why? Whar?");
                }
#endif
                State california = recentState.Value; // haha get it, california is a state haha
                california.spawnedThing = objDest;
                states[objDest.GetInstanceID()] = california;
#if DEBUG
                SceneSaverBL.Log($"Cached spawner state for {spawnedBoard.transform.GetFullPath()} (start @ {california.pointStart}, end @ {california.pointEnd})");
#endif
                recentState = null;
            }

            if (waitingForPlanks.Count != 0)
            {
#if DEBUG
                SceneSaverBL.Log($"Completing UniTask waiter for a plank (there were {waitingForPlanks.Count} UTCSs)");
#endif
                UniTaskCompletionSource<GameObject> taskCompl = waitingForPlanks.Dequeue();
                taskCompl.TrySetResult(spawnedBoard);
                return;
            }

            spawnedPlanks.Enqueue(spawnedBoard);
        }

        public static State GetStateWhenSpawned(ObjectDestructible od)
        {
#if DEBUG
            if (od == null) throw new ArgumentNullException(nameof(od));
#endif

            //todo: make sure this never braeks
            if (states.TryGetValue(od.GetInstanceID(), out var state))
                return state;

#if DEBUG
            Poolee ap = Instances<Poolee>.Get(od.gameObject)!;
            SceneSaverBL.Warn("ObjectDestructible not found in states dict: " + od.transform.GetFullPath());
            SceneSaverBL.Warn("ObjDest spawn index: " + Instances.AllPools.First(p => p._crate == ap.SpawnableCrate)._spawned.IndexOf(ap));
            //SceneSaverBL.Warn("ObjectDestructible not found in states dict: " + od.transform.GetFullPath());
#endif
            throw new KeyNotFoundException();
        }

        /// <summary>
        /// CRITICAL to call before initializing planks <br/>
        /// If not called,accumulated boards will collect in an internal buffer and the wrong planks will be returned when calling <see cref="WaitForAnyBoard"/>
        /// </summary>
        public static void ClearBoardBacklog()
        {
            spawnedPlanks.Clear();
        }

        public static UniTask<GameObject> WaitForAnyBoard()
        {
            UniTaskCompletionSource<GameObject> taskCompleter = new();
            if (spawnedPlanks.Count == 0)
            {
#if DEBUG
                SceneSaverBL.Log("Waiting for board to be spawned!");
#endif
                waitingForPlanks.Enqueue(taskCompleter);
                return taskCompleter.Task;
            }
#if DEBUG
            else
                SceneSaverBL.Log($"There were {spawnedPlanks.Count} spawned planks in the backlog -- returning one now!");
#endif

            return UniTask.FromResult(spawnedPlanks.Dequeue());
        }



        // unfinished method. hopefully wont need?
        //public static UniTask<GameObject> WaitForBoardAttachedTo(Rigidbody)
        //{
        //    UniTaskCompletionSource<GameObject> taskCompleter = new();
        //    if (spawnedPlanks.Count == 0)
        //    {
        //        waitingForPlanks.Enqueue(taskCompleter);
        //        return taskCompleter.Task;
        //    }

        //    return UniTask.FromResult(spawnedPlanks.Dequeue());
        //}
    }

    public static class Constraint
    {
        // avoids (slight) memory leak by uncaching "null" trackers when a new one is created
        private static Dictionary<ConstraintTracker, State> states = new(UnityObjectComparer<ConstraintTracker>.Instance);
        private static Dictionary<ConstraintTracker, Constrainer.ConstraintMode> modes = new(UnityObjectComparer<ConstraintTracker>.Instance);
        internal static void Init()
        {
            Hook.OntoMethod(typeof(Constrainer), nameof(Constrainer.PrimaryButtonUp), CheckConstrainerForJoint);
        }

        static void CheckConstrainerForJoint(Constrainer instance)
        {
            if (instance.mode == Constrainer.ConstraintMode.Remove) return;
            // according to ghidra output, this is an early-bail condition (see: https://cdn.discordapp.com/attachments/656631681406468137/1151258141292498964/image.png)
            if (instance._mb1?._rigidbody == null && instance._mb2?._rigidbody == null) return;
            // this is also an early-bail condition (see: https://cdn.discordapp.com/attachments/656631681406468137/1151260665483374654/image.png)
            if (instance._mb1 == instance._mb2) return;

            GameObject checkForTracker = instance._gO1 ?? instance._gO2;
            ConstraintTracker tracker;
            State state;
            if (checkForTracker == null) return;
            // must use getcomponent because it has no start or awake method to patch 😦
            tracker = checkForTracker.GetComponent<ConstraintTracker>();
            if (!tracker.isHost) tracker = tracker.otherTracker;

            state = new()
            {
                spawnedThing = tracker,
                pointStart = instance._point1,
                pointEnd = instance._point2,
            };

            states[tracker] = state;
            modes[tracker] = instance.mode;
            // cant remove "null" from a dictionary, just ignore the probable memory leak from things being destroyed
            //states.Remove(null!); // this should remove collected things
            //modes.Remove(null!);
        }

        public static State GetStateWhenSpawned(ConstraintTracker ct)
        {
            //todo: make sure this never braeks
            return states[ct];
        }

        public static Constrainer.ConstraintMode GetModeWhenSpawned(ConstraintTracker ct)
        {
            return modes[ct];
        }
    }
}
