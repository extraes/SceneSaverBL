using BoneLib.BoneMenu.Elements;
using SceneSaverBL.Exceptions;
using SceneSaverBL.Interfaces;
using SLZ.Marrow.Pool;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL.Versions;

internal class FailedSaveFile : ISaveFile
{
    Exception exception;
    string path;

    public byte Version => 255;

    public FailedSaveFile(Exception ex)
    {
        this.exception = ex;
    }

    public Task Construct(AssetPoolee[] poolees, ConstraintTracker[] constraints)
    {
        throw new NotImplementedException("A failed save file cannot serialize data");
    }

    public Task Initialize()
    {
        throw new NotImplementedException("A failed save file cannot deserialize data");
    }


    public Task Read(Stream stream)
    {
        throw new NotImplementedException("A failed save file cannot deserialize data");
    }

    public Task Write(Stream stream)
    {
        throw new NotImplementedException("A failed save file cannot serialize data");
    }

    public Task SetFilePath(string filePath)
    {
        this.path = filePath;
        return Task.CompletedTask;
    }

    public void PopulateBoneMenu(MenuCategory category)
    {
        SubPanelElement spe = category.CreateSubPanel(Path.GetFileNameWithoutExtension(path), Color.red);
        SaveUtils.DefaultBoneMenuErrored(spe, GetErrorStr(exception));
    }

    public bool ExistsOnDisk()
    {
        return File.Exists(path);
    }

    private string GetErrorStr(Exception ex)
    {
        return ex switch
        {
            FileNotFoundException fnfe => "File not found",
            InvalidVersionException ive => "Unsupported file ver " + ive.version,
            EndOfStreamException eose => "File unexpectedly ended (too small)",
            UnauthorizedAccessException uae => "File in use/readonly",
            InvalidDataException ide => "Incorrect/corrupted data",
            IOException ioe => "File err " + ioe.Message,
            _ => "Unknown error",
        };
    }
}
