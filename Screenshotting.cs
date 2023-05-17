using Cysharp.Threading.Tasks;
using Jevil;
using SLZ.Bonelab;
using SLZ.VRMK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL;

static internal class Screenshotting
{
    public static async Task<Texture2D> TakeScreenshotWith(Camera cam)
    {
        // depth of 0 causes sorting issues on quest, see: https://discord.com/channels/563139253542846474/724595991675797554/1081754179606953984
        // idk why it doesnt on pc, maybe because pc isnt dogshit :D
        int depth = Utilities.IsPlatformQuest() ? 32 : 0;
        RenderTexture previewTex = new(Prefs.previewSize, Prefs.previewSize, depth);
        cam.targetTexture = previewTex;

        // wait one frame after creating textures - we dont want to halt main thread for too long
        await UniTask.Yield();

#if DEBUG
        Stopwatch sw = Stopwatch.StartNew();
#endif
        // enable hair and disable prefs UI because avatar hair looks good and the preferences view obscures the view if the player is inside the selection area
        Instances<PlayerAvatarArt>.Get(Instances.Player_RigManager.gameObject).EnableHair();
        Instances.Player_RigManager.uiRig.popUpMenu.preferencesPanelView.gameObject.SetActive(false);
        cam.Render();
        Instances.Player_RigManager.uiRig.popUpMenu.preferencesPanelView.gameObject.SetActive(true);
        Instances<PlayerAvatarArt>.Get(Instances.Player_RigManager.gameObject).DisableHair();
#if DEBUG
        SceneSaverBL.Log($"Rendering preview from camera took {sw.ElapsedTicks / 10000f}ms");
#endif
        // rendering is a slow operation, wait another frame
        await UniTask.Yield();
#if DEBUG
        sw.Restart();
#endif
        // convert to T2D because ImageConversion doesnt take RenderTextures
        Texture2D preview = ToTexture2D(previewTex);
#if DEBUG
        SceneSaverBL.Log($"Converting RenderTexture to Texture2D took {sw.ElapsedTicks / 10000f}ms");
        Utilities.InspectInUnityExplorer(preview);
#endif
        return preview;
    }

    // taken from some stackoverflow/unity forum thread
    static Texture2D ToTexture2D(RenderTexture rTex)
    {
        Texture2D tex = new(rTex.width, rTex.height, TextureFormat.RGB24, false);
        RenderTexture.active = rTex;
        tex.ReadPixels(new Rect(0, 0, rTex.width, rTex.height), 0, 0);
        tex.Apply();
        return tex;
    }

    public static async Task PerformEffects(Transform effectsOrigin, Texture2D texture, GameObject cameraFlashPrefab, GameObject polaroidPrefab)
    {
        // transparent effects are expensive on quest - i want this mod to lag as little as possible (but fuck pooling lol)
        if (!Utilities.IsPlatformQuest())
        {
            // div by 15 because the flash cone volumetric is ~18m
            float flashScalar = SelectionZone.Instance.CornerDistance / 15;
            GameObject flash = GameObject.Instantiate(cameraFlashPrefab);
            flash.transform.position = effectsOrigin.position;
            flash.transform.rotation = effectsOrigin.rotation;
            flash.transform.localScale = Vector3.one * flashScalar;
            flash.SetActive(true);
            GameObject.Destroy(flash, 3);

            await UniTask.Delay(950);
        }

        if (!Prefs.disablePolaroid)
        {
            Vector3 pos = effectsOrigin.position + effectsOrigin.forward;
            Quaternion rot = Quaternion.Euler(effectsOrigin.transform.rotation.eulerAngles + new Vector3(-30, 180, 0));
            GameObject newPolaroid = SpawnPolaroidAt(polaroidPrefab, texture, pos, rot);
            GameObject.Destroy(newPolaroid, 20);
        }
    }

    internal static GameObject SpawnPolaroidAt(GameObject polaroidBase, Texture2D texture, Vector3 pos, Quaternion rot)
    {
        GameObject polaroid = GameObject.Instantiate(polaroidBase);
        polaroid.transform.position = pos;
        polaroid.transform.rotation = rot;
        polaroid.GetComponent<SaveIndication>().material.SetTexture(Const.UrpLitMainTexID, texture);
        polaroid.SetActive(true);
        return polaroid;
    }
}
