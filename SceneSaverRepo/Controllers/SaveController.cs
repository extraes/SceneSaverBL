using Microsoft.AspNetCore.Mvc;
using SceneSaverRepo.Data;
using System.Runtime.CompilerServices;

namespace SceneSaverRepo.Controllers;

[Route("api/saves/[action]")]
[ApiController]
public class SaveController : ControllerBase
{
    static readonly byte[] FormatId = { (byte)'S', (byte)'S', (byte)'B', (byte)'L' };

    // cache but dont stop GC
    static readonly ConditionalWeakTable<string, byte[]> files = new();

    [HttpPut]
    [ActionName("uploadBase64")]
    public async Task<IActionResult> UploadSave(string filename, string data, bool temporary = true)
    {
        byte[] bytes = Convert.FromBase64String(data);
        using MemoryStream stream = new(bytes); // using this, so must be async otherwise it'd cause ObjectDisposedException
        return await UploadSave(stream, filename, temporary);
    }
    
    [HttpPut]
    [ActionName("upload")]
    [Consumes("application/octet-stream")]
    public async Task<IActionResult> UploadSave(Stream file, string filename, bool temporary = true)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) if (filename.Contains(c)) return BadRequest("Cannot include invalid path chars");
        if (!filename.EndsWith(".ssbl")) return BadRequest("Incorrect file extension");
        if (filename.Contains("..")) return BadRequest("Cannot include invalid char sequences");

        string ip;
        if (Request.Headers.TryGetValue("cf-connecting-ip", out var ipHeader))
            ip = ipHeader.ToString();
        else
            ip = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(ip))
            ip = "127.0.0.1";

        Program.logger.LogInformation($"Received save from {ip}");

        string hashedOwner = SaveStore.HashIpForOwnerId(ip);

        SceneSaverSaveEntry saveMetadata = new();
        using MemoryStream ms = new();
        await file.CopyToAsync(ms);
        byte[] fileBytes = ms.ToArray();

        if (fileBytes.Length / 1024 > RepoConfig.instance.maxFilesizeKB || fileBytes.Length < 64)
        {
            return BadRequest("Lack of content or incorrect content length");
        }

        for (int i = 0; i < 4; i++)
        {
            if (fileBytes[i + 1] != FormatId[i])
                return BadRequest("Not an SSBL file");
        }
        byte[] hashed = RepoConfig.hasher.ComputeHash(fileBytes);

        saveMetadata.version = fileBytes[0];
        saveMetadata.name = filename;
        saveMetadata.hash = Convert.ToHexString(hashed).ToUpper();
        saveMetadata.tag = await SaveStore.GetOrCreateTag(saveMetadata.hash);
        saveMetadata.expire = temporary ? DateTime.Now + TimeSpan.FromHours(12) : DateTime.MaxValue;
        saveMetadata.owner = hashedOwner;

        if (System.IO.File.Exists(saveMetadata.GetMetafilePath()))
        {
            // skinwalk existing metadata to avoid overwriting download counts & a potentially already-extended expiration
            saveMetadata = await SaveStore.ReadMetadata(saveMetadata.tag);
            if (saveMetadata.expire is not null && saveMetadata.TimeUntilExpired() < TimeSpan.FromHours(12))
            {
                saveMetadata.expire = DateTime.Now + TimeSpan.FromHours(12);
            }

            await SaveStore.WriteMetadata(saveMetadata);

            return Ok(saveMetadata);
        }

        await SaveStore.WriteMetadata(saveMetadata);
        await SaveStore.WriteSave(saveMetadata, fileBytes);

        RepoInfoAccumulator.NewSaveCreated(saveMetadata);
        
        Program.logger.LogInformation($"Save from {ip} has tag {saveMetadata.tag}");

        return Ok(saveMetadata);
    }

    [HttpGet]
    [ActionName("exists")]
    public bool HashOrTagExists(string tag)
    {
        if (tag.Any(c => c == '/' || c == '\\' || c == '.')) return false;
        tag = tag.ToUpper();
        return SaveStore.TagExists(tag);
    }

    [HttpGet]
    [ActionName("info")]
    public async Task<IActionResult> GetInfo(string tag)
    {
        if (tag.Any(c => c == '/' || c == '\\' || c == '.')) return BadRequest("Cannot include invalid chars");
        tag = tag.ToUpper();

        SceneSaverSaveEntry metadata = await SaveStore.ReadMetadata(tag);

        // dont cache this. it will be called a lot.
        //(byte[]? file, SceneSaverSaveEntry metadata) = await SaveStore.GetFileWithTag(tag);

        //if (file is not null)
        //    files.Add(metadata.tag, file);

        return Ok(metadata);
    }

    [HttpGet]
    [ActionName("download")]
    public IActionResult DownloadSave(string tag)
    {
        if (tag.Any(c => c == '/' || c == '\\' || c == '.')) return BadRequest("Cannot include invalid chars");
        tag = tag.ToUpper();

        if (!files.TryGetValue(tag, out var file))
        {
            using FileStream? fs = SaveStore.GetFile(tag);

            if (fs is null) 
                return StatusCode(404);
            using MemoryStream ms = new((int)fs.Length);

            fs.CopyTo(ms);
            byte[] buff = ms.ToArray();
            
            files.Add(tag, buff);
            return File(buff, "application/octet-stream");
        }
        else return File(file, "application/octet-stream");

    }
}
