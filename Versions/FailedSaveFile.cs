using SceneSaverBL.Exceptions;
using SceneSaverBL.Interfaces;

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

    public Task Construct(Poolee[] poolees, ConstraintTracker[] constraints)
    {
        throw new NotImplementedException($"Failed save file ({Path.GetFileName(path)}) cannot construct data to be serialized");
    }

    public Task Initialize()
    {
        throw new NotImplementedException($"Failed save file ({Path.GetFileName(path)}) cannot initialize un-deserialized data");
    }


    public Task Read(Stream stream)
    {
        throw new NotImplementedException($"Failed save file ({Path.GetFileName(path)}) cannot deserialize data");
    }

    public Task Write(Stream stream)
    {
        throw new NotImplementedException($"Failed save file ({Path.GetFileName(path)}) cannot serialize data");
    }

    public Task SetFilePath(string filePath)
    {
        this.path = filePath;
        return Task.CompletedTask;
    }

    public void PopulateBoneMenu(Page page)
    {
        Page failPage = page.CreatePage(Path.GetFileNameWithoutExtension(path), Color.red);
        SaveUtils.DefaultBoneMenuErrored(failPage, GetErrorStr(exception));
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

    public (Bounds, Bounds) GetBoundsForDupeAndDisplay()
    {
        throw new NotImplementedException();
    }
}
