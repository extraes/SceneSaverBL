# SceneSaverBL
## Basically GMod dupes in BONELAB

### You can watch the trailer [here](https://youtu.be/wkUu3JYt0u8)

## What does it save?

 - Spawnables

   - NPCs (cuz theyre spawnables)

   - Giant stacks of props, like the "Dunes of Nimbus" if you spawn a few hundred Nimbus Guns

 - Constraints

   - Constraints to solid/immobile geometry can be optionally loaded, but only if the constraint type is a WELD

   - As a simplification, all constraints will be loaded with their center point(s) being the center of mass of their parent rigidbody.

 - Preview image & area

   - Show off what's in your save! Anything ingame will appear in the preview, including your avatar!

   - The area that was saved will appear when you preview the save, so you know where you left off.

## Preferences

Among various other things, SceneSaverBL manages its preferences/config via JeviLib. Various aspects of SSBL can be adjusted and fine-tuned to find the best experience for *you*. All of these values can be changed in MelonPreferences.cfg or in BoneMenu.

 - **showPreviewLocation** (true/false, default: false)

    - Always shows where the preview image will be taken from, as opposed to only when SSBL's BoneMenu is open (by showing an orange smiley face).

 - **disablePolaroid** (true/false, default: false)

    - When saving, a 'polaroid' of the preview image is spawned. To disable this, set this to true.

 - **filterByLevel** (true/false, default: true)

    - When looking through your locally stored saves, this will filter out saves that are not for your current level (example: a save for LongRun won't show up if you're in Mine Dive).

 - **freezeWhileLoading** (true/false, default: true)

    - Freezes objects while other objects are still loading. This may cause saves to load slower than if it was disabled.

 - **timeSliceMs** (1 to 10, default: 3)

    - Dictates how many milliseconds per-frame are going to be dedicated to 

 - **previewSize** (64 to 2048, default: 256)

    - Dictates the resolution of your preview images. Larger resolutions hitch the game longer, so it's not recommended to set this beyond ~512.

 - **saveChecks** (true/false, default: true)

    - Performs extra checks while saving, to make sure data will be able to be loaded properly (has fallback behavior)

 - **fullsaveOverTime** (true/false, default: true)

    - Attempts to avoid lag spikes by splitting the full-save (that don't occur in quicksaves) processes over time, as opposed to fullsaving/loading each object in its entirety all at once.

 - **loadStaticWelds** (true/false, default: false)

    - Loads WELD constraints that are between the current object and a non-saved object. Use this if you pin an object in place when saving and want it to remain pinned when being loaded.

 - **dontUseStickClick** (true/false, default: false)

    - If true, will move wire when only Grip+Trigger are held, otherwise will require Grip+Trigger+StickClick

## What's next?

I still have ideas for things to add in SSBL, so don't think this is a "set it and forget it" deal for me. I've got more stuff to do.

 - Repo system, so people can upload their saves and download the most popular saves people have shared, all from within the game!

   - This requires a server to host it on, and I'm not made of money<sup>[Citation needed]</sup> so I've set up [a Ko-fi](https://ko-fi.com/extraes) if you want to see it happen.

 - Saving more things!

   - Boards created by the board gun aren't saved, just constraints and poolees, but I'd like to start on getting board saving added someday.

 - Dupes!

   - Right now SSBL saves are locked in the same places they were created at, and I haven't found an elegant solution to allow you to move them around, but I'd like to.

 - Improved constraint saving/loading

   - Constraints currently do not save where they were (position-wise) at save time, but that's mostly just because I didn't feel like spending all the time debugging it, heh. I do want to rectify that.

   - Constraints are also loaded last in the loading process, but they can be loaded as soon as the things they're attached to finish loading.

 - Fusion support!
 
   - **Don't get me wrong! SSBL works with Fusion already!** However, only as the *host*. Right now, there's no way for the *clients* to spawn things even if the host wants to load a client's save, and I want to change that, possibly by allowing the host to "request" a file from a client, and then load it.

 - Optimize save sizes!

   - I'll be the first to admit: I'm pretty wasteful with disk space when it comes to SSBL saves, and there's a few things I could do to cut them down to size. (Like using a lookup table for barcodes and using zip compression)

---

If you like SSBL and want to support it/me/other mods I've made or will make (like BLChaos), you can [support me on Ko-fi](https://ko-fi.com/extraes). If I hit the SSBL repos goal, I'll start working on adding ingame repos to the mod.

SceneSaverBL uses the name from the BONEWORKS mod "SceneSaver". I never used it and didn't check who made it, I just liked the name and wanted to make "GMod dupes but in BONELAB" a reality, because it seemed like a fun programming challenge.

### SceneSaverBL uses two models for the Selection Wire tutorial:

 1. The left Oculus Quest 2 controller model is from:

    - [Quest 2 Controller Low Mid and High Poly by BlackCube on Sketchfab](https://sketchfab.com/3d-models/quest-2-controller-low-mid-and-high-poly-9f02b77cc27148c986315674c7ed106d)

 2. The left Valve Index controller model is from:
 
    - [Valve Index Controller Left by F53 on Sketchfab](https://sketchfab.com/3d-models/valve-index-controller-left-24cc19b6c68d4cdba24e8424a1321658)