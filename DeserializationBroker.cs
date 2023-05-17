using SceneSaverBL.Exceptions;
using SceneSaverBL.Interfaces;
using SceneSaverBL.Versions;
using SLZ.Marrow.Pool;
using SLZ.Marrow.Utilities;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL;

internal static class DeserializationBroker
{
    public static async Task<ISaveFile> CreateSaveFromFile(string path)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);

            byte version = await SaveUtils.CheckFormatIdentifier(fs);

#if DEBUG
            SceneSaverBL.Log($"Save {Path.GetFileName(path)} is of version {version}");
#endif

            ISaveFile isf = CreateFileVersion(fs, version);
            await isf.SetFilePath(path);
            return isf;
        }
        catch (Exception ex)
        {
            SceneSaverBL.Warn(ex);
            return new FailedSaveFile(ex);
        }
    }

    private static ISaveFile CreateFileVersion(FileStream fs, byte version)
    {
        return version switch
        {
            // todo: fix SSBL V4 files breaking BoneMenu when hitting Read Info
            4 => new Versions.Version4.SaveFile(), // its method of reading files is fucked - dont bother passing filestream because its data is scattered everywhere, no "header" exists
            5 => new Versions.Version5.SaveFile5(),
            6 => new Versions.Version6.SaveFile6(),
            _ => throw new InvalidVersionException(version),
        };
    }
}
