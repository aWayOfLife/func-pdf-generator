using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using jsreport.Local;
using jsreport.Binary;
using jsreport.Types;
using Newtonsoft.Json;
using System;



namespace GeneratePdfFunction
{
    public static class GeneratePdfFunction
    {
        private static readonly ILocalWebServerReportingService _localWebServerReportingService;
        private static readonly Task _localServerStartupTask;

        static GeneratePdfFunction()
        {
            _localWebServerReportingService = new LocalReporting()
                .UseBinary(JsReportBinary.GetBinary())
                .KillRunningJsReportProcesses()
                .Configure(cfg =>
                {
                    cfg.TempDirectory = Path.Combine(Directory.GetCurrentDirectory(), "jsreport", "temp");
                    cfg.FileSystemStore().BaseUrlAsWorkingDirectory();
                    cfg.AllowLocalFilesAccess = true;
                    cfg.TemplatingEngines = new TemplatingEnginesConfiguration()
                    {
                        Timeout = 600000,
                    };
                    cfg.Extensions = new ExtensionsConfiguration()
                    {
                        Scripts = new ScriptsConfiguration()
                        {
                            Timeout = 600000,
                        }
                    };
                    cfg.Chrome = new ChromeConfiguration()
                    {
                        Timeout = 600000
                    };
                    return cfg;
                })
                .AsWebServer()
                .RedirectOutputToConsole()
                .Create();

            _localServerStartupTask = _localWebServerReportingService.StartAsync();
        }

        [FunctionName("GeneratePdfFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "generate-pdf/{templateName}")] HttpRequest req,
            string templateName,
            ILogger log)
        {
            log.LogInformation($"Generating PDF using jsreport template: {templateName}");

            await _localServerStartupTask;

            string body = await new StreamReader(req.Body).ReadToEndAsync();

            object data;
            try
            {
                data = JsonConvert.DeserializeObject(body);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Invalid JSON in request body.");
                return new BadRequestObjectResult("Invalid JSON in request body.");
            }

            try
            {
                var report = await _localWebServerReportingService.ReportingService.RenderByNameAsync(templateName, data);

                return new FileStreamResult(report.Content, "application/pdf")
                {
                    FileDownloadName = $"{templateName}.pdf"
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Failed to render template '{templateName}'");
                return new NotFoundObjectResult($"Template '{templateName}' not found or failed to render.");
            }
        }

    }
}
