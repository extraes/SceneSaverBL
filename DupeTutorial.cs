using Jevil.Waiting;
using System;

namespace SceneSaverBL;


[RegisterTypeInIl2Cpp]
public class DupeTutorial : MonoBehaviour
{
    public DupeTutorial(nint ptr) : base(ptr) { }
    const string PLAYER_PREFS_KEY = "SSBL_Shown_DupeTutorial";
    static bool tutorialCurrentlyActive;

    double startTime;
    Transform handL;
    Transform handR;
    // Use this for initialization
    void Start()
    {
        tutorialCurrentlyActive = !hideFlags.HasFlag(HideFlags.DontUnloadUnusedAsset);
        transform.position = Player.Head.position + Player.Head.forward * 50;
        startTime = Time.timeAsDouble;
        // jank departure from controllertutorial but the anim for dupe tutorial already handles hiding itself
        CallDelayed.CallAction(() => Destroy(gameObject), 21);
        handL = Player.LeftHand.transform;
        handR = Player.RightHand.transform;
    }

    void Update()
    {
        Transform head = Player.Head;
        Vector3 handMidpoint = Vector3.Lerp(handL.position, handR.position, 0.5f);
        Vector3 dirAbnormal = head.forward + new Vector3(0, 0.25f, 0); // dirNotNormalized wasnt as short. lol.
        Vector3 dir = dirAbnormal.normalized;
        dir.y /= 2;
        Vector3 inFrontOfPlayer = head.position + 1.5f * dir;

        // so that it doesnt rotate for the first 5 seconds. this is so fucking hacky lmao
        //double rotAsDouble = Math.Max(0, Time.timeAsDouble - startTime - 5) * Const.FPI % (2 * Const.FPI);
        Vector3 posDelta = head.position - transform.position;
        /*posDelta.x =*/
        posDelta.y = 0;
        Quaternion rotation = Quaternion.LookRotation(posDelta);
        //Vector3 eulerRot = Quaternion.ToEulerAngles(rotation);
        //rotation = Quaternion.Euler(Vector3.ProjectOnPlane(eulerRot, Vector3.up));
        Vector3 desiredPos = Vector3.Lerp(inFrontOfPlayer, handMidpoint, 0.25f);
        Vector3 position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * 5);
        transform.SetPositionAndRotation(position, rotation);
        //transform.position = position;
    }

    void OnDestroy()
    {
        tutorialCurrentlyActive = false;
    }

    public static void ShowIfUnseen()
    {
        if (!PlayerPrefs.HasKey(PLAYER_PREFS_KEY))
        {
            Show();

#if DEBUG
            SceneSaverBL.Log("Showing dupe tutorial");
#endif
        }
    }

    public static void Show()
    {
        PlayerPrefs.TrySetInt(PLAYER_PREFS_KEY, 1);
        AsyncUtilities.WrapNoThrow(ShowImpl).RunOnFinish(SceneSaverBL.ErrIfNotNull);
    }

    static async Task ShowImpl()
    {
        GameObject tutorialPrefab = await Assets.Prefabs.DupeTutorial.GetAsync();
        if (tutorialCurrentlyActive)
            return;

        GameObject instance = GameObject.Instantiate(tutorialPrefab);
        instance.AddComponent<DupeTutorial>();
        instance.SetActive(true);
    }
}
