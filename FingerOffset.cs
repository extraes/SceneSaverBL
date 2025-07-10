using BoneLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSLZ.Bonelab;
using Jevil.IMGUI;
using Jevil.PostProcessing;
using System.Net.WebSockets;

namespace SceneSaverBL;

internal static class FingerOffset
{
    const float ANT_SIZE_DEFAULT = 1;
    enum BlendModes
    {
        Alpha,
        PreMultiply,
        Additive,
        Multiply,
    }
    static readonly ShaderProperty<float> antSize = "_AntSize";
    static readonly ShaderProperty<int> blendMode = "_Blend";

#if DEBUG
    static float distResult;
    static float light;
    static int lastRecheckCount;
    static FingerOffset()
    {
        DebugDraw.TrackVariable("last dist", GUIPosition.TOP_RIGHT, () => distResult);
        DebugDraw.TrackVariable("recheck", GUIPosition.TOP_RIGHT, () => lastRecheckCount);
        DebugDraw.TrackVariable("ssbl light", GUIPosition.TOP_RIGHT, () => light);
    }
#endif

    static readonly Vector3[] directions = new Vector3[]
    {
        // - 0 +
        new(-1, 0, -1),
        new(-1, 0, 0),
        new(-1, 0, 1),
        new(0, 0, -1),
        // new(0, 0, 0), not a direction
        new(0, 0, 1),
        new(1, 0, -1),
        new(1, 0, 0),
        new(1, 0, 1),
    };

    static readonly float[] directionDistances = new float[directions.Length];

    static void CalculateDirectionDistances()
    {
        float x = wsSaveExtents.x;
        float z = wsSaveExtents.y;
        float hyp = Mathf.Sqrt(x * x + z * z);
        directionDistances[0] = hyp; // -x -z
        directionDistances[1] = x; // +x
        directionDistances[2] = hyp; // -x +z
        directionDistances[3] = z; // -z
        directionDistances[4] = z; // +z
        directionDistances[5] = hyp; // +x -z
        directionDistances[6] = x; // +x
        directionDistances[7] = hyp; // +x +z
    }

    static readonly Vector3[] scratchDirections = new Vector3[directions.Length];
    static readonly float[] scratchDistances = new float[directions.Length];
    static readonly float[] distanceHitPoints = new float[directions.Length];
    static readonly byte[] directionComplements = directions.Select(dir => (byte)Array.IndexOf(directions, -dir)).ToArray(); // use as lookup table

    static float timerFollowSpeed = 50f;

    static Transform timer;
    static Animation timerAnim;
    static Renderer timerRend;
    static Il2CppStructArray<Vector3> timerDirScratch = new(1);
    static Il2CppStructArray<Color> timerColScratch = new(1);
    static GameObject line;
    static Transform lineTransform;
    static Transform indexFinger;
    static Material dupeMat;
    static BlendModes? currBlendMode;
    public static Vector2 wsSaveExtents = Vector2.one; // EXTENTS, not SIZE.

    static bool forceFadeoutTimer = false;

    static Transform Timer
    {
        get => timer;
        set
        {
#if DEBUG
            SaveChecks.ThrowIfDefault(value);
#endif
            timer = value;
            timerAnim = value.GetComponent<Animation>();
            timerRend = value.GetChild(0).GetComponent<Renderer>();
        }
    }
    static Animation TimerAnim
    {
        get
        {
            if (timerAnim == null && Timer != null) // should never be true, but just in case
                timerAnim = Timer.GetComponent<Animation>();
            return timerAnim;
        }
    }
    static Renderer TimerRend
    {
        get
        {
            if (timerRend == null && Timer != null)
                timerRend = Timer.GetChild(0).GetComponent<Renderer>();
            return timerRend;
        }
    }
    static Il2CppStructArray<Vector3> TimerDirScratch
    {
        get
        {
            if (timerDirScratch is null || timerDirScratch.WasCollected)
                timerDirScratch = new (1);
            return timerDirScratch;
        }
    }
    static Il2CppStructArray<Color> TimerColScratch
    {
        get
        {
            if (timerColScratch is null || timerColScratch.WasCollected)
                timerColScratch = new(1);
            return timerColScratch;
        }
    }
    static GameObject Line
    {
        get => line;
        set
        {
#if DEBUG
            SaveChecks.ThrowIfDefault(value);
#endif
            line = value;
            lineTransform = value.transform;
        }
    }
    static Transform LineTransform
    {
        get
        {
            if (lineTransform == null && Line != null)
                lineTransform = Line.transform;
            return lineTransform;
        }
    }
    static Transform IndexFinger
    {
        get
        {
            if (indexFinger == null)
                indexFinger = GetIndex();
            return indexFinger;
        }
    }
    static Material DupeMat
    {
        get
        {
            if (dupeMat == null && Line != null)
                dupeMat = LineTransform.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
            return dupeMat;
        }
    }

    public static void Init()
    {
        Hooking.OnLevelLoading += (_) => currBlendMode = null;

    }

    public static void Begin()
    {
        forceFadeoutTimer = false;
        if (Line != null)
        {
            Line.SetActive(true);
            return;
        }

#if DEBUG
        int startFrame = Time.renderedFrameCount;
        double startTime = Time.realtimeSinceStartupAsDouble;
#endif
        Assets.Prefabs.Laser.GetAsync().RunOnFinish(go =>
            {
#if DEBUG
                SceneSaverBL.Log($"Loaded laser after {Time.renderedFrameCount - startFrame} frames ({Time.realtimeSinceStartupAsDouble - startTime:0.000} sec)");
#endif
                Line = GameObject.Instantiate(go);
                Line.SetActive(true);
            }
        );

        Assets.Prefabs.GroundCircle.GetAsync().RunOnFinish(go =>
        {
#if DEBUG
            SceneSaverBL.Log($"Loaded timer/groundcircle after {Time.renderedFrameCount - startFrame} frames ({Time.realtimeSinceStartupAsDouble - startTime:0.000} sec)");
#endif
            GameObject timer = GameObject.Instantiate(go);
            Timer = timer.transform;
            timer.SetActive(false);

            TimerAnim.Play();
            int stateCount = TimerAnim.GetStateCount();
            for (int i = 0; i < stateCount; i++)
            {
                TimerAnim.GetStateAtIndex(i).speed = 0;
                TimerAnim.GetStateAtIndex(i).time = 0;
            }
            TimerAnim.Stop();
        });
    }

    public static async Task MenuGetPosition()
    {
        SceneSaverBL.desiredDupePos = null;
        Page currPage = Menu.CurrentPage;

        PopUpMenuView popup = Player.UIRig.popUpMenu;
        UIControllerInput input = popup._lastCursor;
        

        popup.Deactivate();
        Begin();
        while (!SceneSaverBL.desiredDupePos.HasValue)
        {
            await UniTask.Yield();
        }
        End();

        // yield between every UI-changing action to avoid race conditions
        popup.Activate(Player.ControllerRig.m_head, Player.PhysicsRig.m_chest, input, input.isLeft ? Player.LeftController : Player.RightController);
        await UniTask.Yield();
        popup.BypassToPreferences();

        PreferencesPanelView ppv = popup.preferencesPanelView;
        int idxBm = ppv.pages.Length - 1;
        for (int i = 0; i < ppv.pages.Length; i++)
        {
            bool isBoneMenu = Instances<BoneLib.BoneMenu.UI.GUIMenu>.Has(ppv.pages[i]);
            if (isBoneMenu)
            {
                idxBm = i;
                break;
            }
        }

        Player.UIRig.popUpMenu.preferencesPanelView.PAGESELECT(idxBm);
        await UniTask.Yield();
        Menu.OpenPage(currPage);
        await UniTask.Yield();
    }

    public static void End()
    {
#if DEBUG
        SceneSaverBL.Log("Ending FingerOffset dupe position acquisition!");
#endif

        if (Line != null)
            Line.SetActive(false);
        if (Timer != null)
        {
            forceFadeoutTimer = true;
            //Timer.position = default;
            //Timer.gameObject.SetActive(false);
        }
    }

    public static void Fadeout()
    {
        if (TimerAnim == null)
            return;

        int stateCount = TimerAnim.GetStateCount();
        for (int i = 0; i < stateCount; i++)
        {
            TimerAnim.GetStateAtIndex(i).speed = -1;
        }
    }

    internal static void OnUpdate()
    {
        if (Line == null || !Line.active || Timer == null)
            return;

        Transform startPoint = IndexFinger;
        Vector3 forward = (startPoint.position - startPoint.parent.parent.position).normalized;
#if DEBUG
        lastRecheckCount = 0;
#endif
        
        const float MAX_DIST = 50f;
        float dist = Physics.Raycast(startPoint.position, forward, out RaycastHit hit, MAX_DIST) ? hit.distance : MAX_DIST;
        for (int i = 0; i < 10; i++)
        {
            if (dist > 0.05f)
                break;

#if DEBUG
            lastRecheckCount++;
#endif
            dist = Physics.Raycast(hit.point + (forward * 0.05f), forward, out hit, MAX_DIST) ? hit.distance : MAX_DIST;
        }

#if DEBUG
        distResult = dist;
#endif

        UpdateLine(startPoint, forward, dist);

        if (dist == 0 || dist == MAX_DIST)
        {
            return;
        }

        //todo: make the "timer" graphic display a red thingy when an invalid surface is hit (too far from level)

        UpdateTimer(hit.point, hit.normal, out Vector3? completedAt);

        if (completedAt.HasValue)
        {
#if DEBUG
            SceneSaverBL.Log("Dupe line completed @ " + completedAt.Value);
#endif
            SceneSaverBL.desiredDupePos = completedAt.Value;
            Fadeout();
        }
    }

    private static void UpdateLine(Transform startPoint, Vector3 forward, float dist)
    {
        antSize.SetOn(DupeMat, ANT_SIZE_DEFAULT / dist);
        Line.transform.position = startPoint.position;
        Line.transform.forward = forward;
        Line.transform.localScale = new Vector3(1, 1, dist);
    }

    private static void UpdateTimer(Vector3 hitPoint, Vector3 normal, out Vector3? completedAt)
    {
        completedAt = null;
        bool completed = false;
        bool aimedAtFloor = Vector3.Dot(normal, Vector3.up) > 0.8f;

        HandPoseAnimator hand = Prefs.rightHandDupeLine ? Player.RightHand.Animator : Player.LeftHand.Animator;
        bool isIndex = (Prefs.rightHandDupeLine ? Player.RightController : Player.LeftController).Type == Il2CppSLZ.Marrow.Input.XRControllerType.Index;

        float gripSum = hand._currentMiddle + hand._currentRing + hand._currentPinky;
        bool grippin = gripSum > 2.7f;

        float thumb = hand._currentThumb;
        bool thumbUp = thumb < 0.1f && !forceFadeoutTimer; // lockout when closing
#if DEBUG
        thumbUp = thumbUp || Input.GetKey(KeyCode.Keypad0);
#endif
        bool handInPosition = grippin && thumbUp;
        if (handInPosition && hand._currentIndex < 0.15f && isIndex)
            hand._currentIndex = 0; // Snap to 0 for a more predictable positioning experience for index users


        int stateCount = TimerAnim.GetStateCount();
        for (int i = 0; i < stateCount; i++)
        {
            AnimationState state = TimerAnim.GetStateAtIndex(i);
            float preTime = state.time;
            float time = aimedAtFloor && handInPosition ? preTime + Time.deltaTime : preTime - Time.deltaTime;
            float clamped = Mathf.Clamp01(time);

            if (grippin && preTime == clamped) continue;
            if (preTime == 0)
            {
                TimerAnim.Sample();
                Timer.gameObject.SetActive(true);
                state.speed = 1;
                TimerAnim.Play();
            }

            state.time = clamped;
#if DEBUG
            //SceneSaverBL.Log("Set animation time to " + state.time + " from " + preTime);
#endif
            if (clamped == 1 && grippin)
                completed = true;

            if (clamped == 0)
            {
                Timer.gameObject.SetActive(false);
                TimerAnim.Stop();
                return;
            }
        }

        if (!Prefs.dontAdjustDupePos)
            AdjustPosFromWall(ref hitPoint, ref normal);

        Vector3 currPos = Timer.position;
        if (currPos == default)
        {
            Timer.position = hitPoint;
            timer.rotation = Quaternion.LookRotation(Vector3.forward, normal); // this should really be raycast down to see what the floor normal is
            return;
        }

        Vector3 targetPos = hitPoint + (normal * 0.01f);
        float dist = aimedAtFloor ? Vector3.Distance(currPos, targetPos) : 0f;
        Vector3 newPos = Vector3.Lerp(currPos, targetPos, Time.deltaTime * timerFollowSpeed * dist);

        if (completed)
            completedAt = newPos;
        Timer.position = newPos;
        if (aimedAtFloor)
            Timer.rotation = Quaternion.LookRotation(Vector3.forward, normal); // this should really be raycast down to see what the floor normal is
        if (newPos != currPos)
        {
            // this doesnt work. the probe color is almost always incomprehensible, like with negative numbers and numbers in the fucking 30s.
//            LightProbes.GetInterpolatedProbe(newPos, TimerRend, out SphericalHarmonicsL2 sh2);
//            TimerDirScratch[0] = newPos;
//            SphericalHarmonicsL2.EvaluateInternal(ref sh2, TimerDirScratch, TimerColScratch);
//            Color c = TimerColScratch[0];
//            float v = c.r + c.g + c.b;
//            //Color.RGBToHSV(TimerColScratch[0], out _, out _, out float v);
//            light = v;
//            BlendModes desiredBlendMode = v < 0.3f ? BlendModes.Additive : BlendModes.Multiply;
            
//            if (!currBlendMode.HasValue || currBlendMode.Value != desiredBlendMode)
//            {
//#if DEBUG
//                SceneSaverBL.Log($"Setting blend mode to {desiredBlendMode} from {(currBlendMode.HasValue ? currBlendMode.Value.ToString() : "<unk>")}, color was {c}");
//#endif
//                blendMode.SetOn(TimerRend.sharedMaterial, (int)desiredBlendMode);
//                currBlendMode = desiredBlendMode;
//            }
        }
    }

    private static void AdjustPosFromWall(ref Vector3 hitPoint, ref Vector3 normal)
    {
        // ive decided to leave half-implemented wsSaveExtents & "distance" fields alone
        // basically this is just to avoid the position selector from clipping into walls, not for actual dupe placement
        const float WALL_DIST = 0.5f;
        int adjustCount = Utilities.IsPlatformQuest() ? 4 : 8;
        for (int i = 0; i < adjustCount; i++) // timeout after too many tries
        {
            Vector3 physcastStart = hitPoint + (normal * 0.01f);
            bool anyWalls = false;
            bool bothBlocked = false;
            (float dist, int idx) minInfo = (1, 0);
            for (int j = 0; j < scratchDirections.Length; j++)
            {
                Vector3 localDir = directions[(j + i) % directions.Length]; // "rotate" directions to avoid getting stuck in a corner
                Vector3 normalAligned = Vector3.Cross(normal, localDir);
                scratchDirections[j] = normalAligned; // aligns them to the normal of the surface that was hit

                bool hit = Physics.Raycast(physcastStart, normalAligned, out RaycastHit hitInfo, WALL_DIST);
                distanceHitPoints[j] = hit ? hitInfo.distance : default;

                if (hit)
                {
                    anyWalls = true;
                    if (minInfo.dist > hitInfo.distance)
                        minInfo = (hitInfo.distance, j);
                }
            }

            if (!anyWalls)
                break;

            physcastStart -= scratchDirections[minInfo.idx] * (WALL_DIST - minInfo.dist);

            if (Physics.Raycast(physcastStart, Vector3.down, out RaycastHit adjustHitInfo, WALL_DIST)) // 0.5m should be far enough to hit the floor
            {
                if (Vector3.Distance(hitPoint, adjustHitInfo.point) < 0.001f)
                    break;
#if DEBUG
                SceneSaverBL.Log($"Adjusted laser point to {adjustHitInfo.point} from {hitPoint} ({Vector3.Distance(adjustHitInfo.point, hitPoint):0.000} meters away) on iter {i}");
#endif
                hitPoint = adjustHitInfo.point;
                normal = adjustHitInfo.normal;
            }

            if (bothBlocked)
            {
#if DEBUG
                SceneSaverBL.Log($"Both sides found to be blocked on iter {i}");
#endif
                break;
            }
        }
    }

    static Transform GetIndex()
    {
        ArtRig artRig = Player.PhysicsRig.artOutput;
        HandPoseAnimator handAnimator = Prefs.rightHandDupeLine ? artRig._rightAnimatorHand : artRig._leftAnimatorHand;
        return handAnimator.index3.transform.GetChild(0);
    }
}
