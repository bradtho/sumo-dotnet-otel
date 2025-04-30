using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Context.Propagation;
using System.Linq;
using System;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Threading.Tasks;

namespace OpenTelemetryMvcApp
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // If your application is .NET Standard 2.1 or above, and you are using an insecure (http) endpoint,
            // the following switch must be set before adding Exporter.
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            services.AddControllersWithViews();

            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddConsole();
                loggingBuilder.AddOpenTelemetry(options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                    options.ParseStateValues = true;
                    options.AddOtlpExporter(exporterOptions =>
                    {
                        exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                });
            });

            services.AddDbContext<AppDbContext>(options =>
                options
                    .UseSqlServer(Configuration.GetConnectionString("DefaultConnection"))
                    .EnableSensitiveDataLogging()
                    .LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information)
            );

            services.AddOpenTelemetry()
                .WithTracing(builder => builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("OpenTelemetryMvcApp"))
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                    })
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddOtlpExporter(exporterOptions =>
                    {
                        exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
                    })
                    .AddConsoleExporter()
                )

                .WithMetrics(metrics => metrics
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("OpenTelemetryMvcApp"))
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddOtlpExporter(exporterOptions =>
                    {
                        exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
                    })
                );
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });

            // Ensure database is created and migrations are applied
            DatabaseInitializer.Initialize(app);
        }
    }

    public static class DatabaseInitializer
    {
        public static void Initialize(IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                Console.WriteLine("Applying migrations...");
                context.Database.Migrate();

                if (!context.Items.Any())
                {
                    Console.WriteLine("Seeding sample data...");
                    context.Items.AddRange(
                        new Item { Name = "Sample Item 1" },
                        new Item { Name = "Sample Item 2" },
                        new Item { Name = "Sample Item 3" }
                    );
                    context.SaveChanges();
                    Console.WriteLine("Sample data seeded.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database initialization failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }
    }

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Item> Items { get; set; }
    }

    public class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("[controller]")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok("Healthy");
    }

    [ApiController]
    [Route("[controller]")]    
    public class ItemsController : ControllerBase
    {
        private static readonly ActivitySource ActivitySource = new("OpenTelemetryMvcApp.ItemsController");
        private readonly AppDbContext _context;

        public ItemsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index()
        {
            using var activity = ActivitySource.StartActivity("Index");
            try
            {
                Console.WriteLine("Fetching all items...");
                var items = _context.Items.ToList();
                Console.WriteLine($"Fetched {items.Count} items.");
                return Ok(items);
            }
            catch (Exception ex)
            {
                activity?.SetTag("error", true);
                activity?.SetTag("exception.message", ex.Message);
                Console.WriteLine($"Error fetching items: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving items.");
            }
        }

        [HttpPost]
        public IActionResult Create([FromBody] Item item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Name))
            {
                Console.WriteLine("Invalid item payload received.");
                return BadRequest("Invalid item.");
            }

            using var activity = ActivitySource.StartActivity("CreateItem");

            try
            {
                Console.WriteLine($"Creating new item: {item.Name}");
                _context.Items.Add(item);
                _context.SaveChanges();
                Console.WriteLine($"Item created with ID {item.Id}");
                return CreatedAtAction(nameof(Index), new { id = item.Id }, item);
            }
            catch (DbUpdateException dbEx)
            {
                activity?.SetTag("error", true);
                activity?.SetTag("exception.message", dbEx.Message);
                Console.WriteLine($"Database error: {dbEx.Message}");
                return StatusCode(500, "A database error occurred.");
            }
            catch (Exception ex)
            {
                activity?.SetTag("error", true);
                activity?.SetTag("exception.message", ex.Message);
                Console.WriteLine($"Error creating item: {ex.Message}");
                return StatusCode(500, "An unexpected error occurred.");
            }
        }

        [HttpGet("deadlock-error")]
        public IActionResult GenerateDeadlockError()
        {
            using var activity = ActivitySource.StartActivity("GenerateDeadlockError");
            try
            {
                var connection = _context.Database.GetDbConnection();
                using var command1 = connection.CreateCommand();
                using var command2 = connection.CreateCommand();
                if (connection.State != ConnectionState.Open)
                {
                    connection.Open();
                }
                command1.CommandText = "BEGIN TRANSACTION; UPDATE Items SET Name = Name WHERE Id = 1; WAITFOR DELAY '00:00:05';";
                command2.CommandText = "BEGIN TRANSACTION; UPDATE Items SET Name = Name WHERE Id = 2; WAITFOR DELAY '00:00:05';";
                Task t1 = Task.Run(() => command1.ExecuteNonQuery());
                Task t2 = Task.Run(() => command2.ExecuteNonQuery());
                Task.WaitAll(t1, t2);
                return Ok("Deadlock simulation completed.");
            }
            catch (Exception ex)
            {
                activity?.SetTag("error", true);
                activity?.SetTag("exception.message", ex.Message);
                Console.WriteLine($"Intentional deadlock: {ex.Message}");
                return StatusCode(500, "Intentional deadlock error occurred.");
            }
        }

        [HttpGet("connection-pool-error")]
        public IActionResult GenerateConnectionPoolExhaustion()
        {
            using var activity = ActivitySource.StartActivity("GenerateConnectionPoolExhaustion");
            try
            {
                var tasks = Enumerable.Range(0, 200).Select(async _ =>
                {
                    var connection = new SqlConnection(_context.Database.GetConnectionString());
                    await connection.OpenAsync();
                    await Task.Delay(5000);
                    await connection.CloseAsync();
                });
                Task.WaitAll(tasks.ToArray());
                return Ok("Connection pool exhaustion test completed.");
            }
            catch (Exception ex)
            {
                activity?.SetTag("error", true);
                activity?.SetTag("exception.message", ex.Message);
                Console.WriteLine($"Intentional connection pool exhaustion: {ex.Message}");
                return StatusCode(500, "Intentional connection pool exhaustion error occurred.");
            }
        }

        [HttpGet("transaction-error")]
        public IActionResult GenerateTransactionError()
        {
            using var activity = ActivitySource.StartActivity("GenerateTransactionError");
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                _context.Items.Add(new Item { Name = "TxItem1" });
                _context.SaveChanges();
                throw new Exception("Simulated failure after first commit");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                activity?.SetTag("error", true);
                activity?.SetTag("exception.message", ex.Message);
                Console.WriteLine($"Intentional transaction failure: {ex.Message}");
                return StatusCode(500, "Intentional transaction failure occurred.");
            }
        }

        [HttpGet("constraint-error")]
        public IActionResult GenerateConstraintError()
        {
            using var activity = ActivitySource.StartActivity("GenerateConstraintError");
            try
            {
                var item = new Item { Id = 1, Name = "DuplicateId" }; // Assuming ID=1 already exists
                _context.Items.Add(item);
                _context.SaveChanges();
                return Ok("Constraint violation test completed.");
            }
            catch (Exception ex)
            {
                activity?.SetTag("error", true);
                activity?.SetTag("exception.message", ex.Message);
                Console.WriteLine($"Intentional constraint violation: {ex.Message}");
                return StatusCode(500, "Intentional constraint violation occurred.");
            }
        }

        [HttpGet("timeout-error")]
        public IActionResult GenerateTimeoutError()
        {
            using var activity = ActivitySource.StartActivity("GenerateTimeoutError");
            try
            {
                var connection = _context.Database.GetDbConnection();
                using var command = connection.CreateCommand();
                if (connection.State != ConnectionState.Open)
                {
                    connection.Open();
                }
                command.CommandText = "WAITFOR DELAY '00:00:30';"; // Intentionally long-running query
                command.CommandTimeout = 1; // Force timeout
                command.ExecuteNonQuery();
                return Ok("Timeout test completed.");
            }
            catch (Exception ex)
            {
                activity?.SetTag("error", true);
                activity?.SetTag("exception.message", ex.Message);
                Console.WriteLine($"Intentional timeout: {ex.Message}");
                return StatusCode(500, "Intentional timeout error occurred.");
            }
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    var url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
                    if (!string.IsNullOrEmpty(url))
                    {
                        webBuilder.UseUrls(url);
                    }

                    webBuilder.UseStartup<Startup>();
                });
    }
}
