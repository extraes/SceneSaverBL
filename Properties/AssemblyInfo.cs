using MelonLoader;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle(SceneSaverBL.BuildInfo.Name)]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany(SceneSaverBL.BuildInfo.Company)]
[assembly: AssemblyProduct(SceneSaverBL.BuildInfo.Name)]
[assembly: AssemblyCopyright("Created by " + SceneSaverBL.BuildInfo.Author)]
[assembly: AssemblyTrademark(SceneSaverBL.BuildInfo.Company)]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
//[assembly: Guid("")]
[assembly: AssemblyVersion(SceneSaverBL.BuildInfo.Version)]
[assembly: AssemblyFileVersion(SceneSaverBL.BuildInfo.Version)]
[assembly: NeutralResourcesLanguage("en")]
[assembly: MelonInfo(typeof(SceneSaverBL.SceneSaverBL), SceneSaverBL.BuildInfo.Name, SceneSaverBL.BuildInfo.Version, SceneSaverBL.BuildInfo.Author, SceneSaverBL.BuildInfo.DownloadLink)]


// Create and Setup a MelonModGame to mark a Mod as Universal or Compatible with specific Games.
// If no MelonModGameAttribute is found or any of the Values for any MelonModGame on the Mod is null or empty it will be assumed the Mod is Universal.
// Values for MelonModGame can be found in the Game's app.info file or printed at the top of every log directly beneath the Unity version.
[assembly: MelonGame("Stress Level Zero", "BONELAB")]