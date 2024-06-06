
namespace SceneSaverRepo
{
    public class Program
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        internal static ILogger logger;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddMvc(opt => opt.InputFormatters.Insert(0, new RawRequestBodyFormatter("application/octet-stream")));

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            logger = app.Logger;

            SaveStore.CleanupExpiredFiles().GetAwaiter().GetResult();

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();
            
            app.Run(RepoConfig.instance.url);
        }
    }
}