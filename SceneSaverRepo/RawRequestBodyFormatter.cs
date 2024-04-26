using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace SceneSaverRepo;


// https://stackoverflow.com/questions/68939075/accepting-binary-data-in-asp-net-controller
public class RawRequestBodyFormatter : InputFormatter
{
    string contentType;

    public RawRequestBodyFormatter(string mimeType)
    {
        contentType = mimeType;
        SupportedMediaTypes.Add(new MediaTypeHeaderValue(mimeType));
    }


    /// <summary>
    /// Allow image/bmp and no content type to
    /// be processed
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public override bool CanRead(InputFormatterContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var contentType = context.HttpContext.Request.ContentType;
        if (string.IsNullOrEmpty(contentType) || contentType == this.contentType)
            return true;

        return false;
    }

    /// <summary>
    /// Handle bmp images
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        var request = context.HttpContext.Request;
        var contentType = context.HttpContext.Request.ContentType;

        if (string.IsNullOrEmpty(contentType) || contentType == this.contentType)
        {
            return await InputFormatterResult.SuccessAsync(request.Body);
        }

        return await InputFormatterResult.FailureAsync();
    }
}

