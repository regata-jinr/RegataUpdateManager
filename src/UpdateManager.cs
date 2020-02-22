using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Xml.Linq;
using Octokit;
using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Regata.Utilities
{

  public interface IUpdateManager
  {
    void CreateRelease();
    Task UploadReleaseToGithub();
    Task UpdateCurrentProject();

  }
  public class UpdateManager : IUpdateManager
  {
    public readonly string ReleaseTag;
    public readonly string ReleaseTitle;
    public readonly string ReleaseNotes;
    public readonly string PackageId;
    public readonly string Version;
    public readonly string RepositoryUrl;
    private readonly string _path;
    private readonly XElement XmlProj;
    private IConfiguration Configuration { get; set; }

    public UpdateManager(string project = "", int verboseLevel = 1)
    {

      if (string.IsNullOrEmpty(project) || !File.Exists(project))
      {
        string[] projects = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
        if (project.Any())
          project = projects[0];
        else
          throw new FileNotFoundException($"*.csproj file not found in current directory - '{Directory.GetCurrentDirectory()}'");
      }

      _path = Path.GetDirectoryName(project);

      Configuration = new ConfigurationBuilder()
                      .SetBasePath(AppContext.BaseDirectory)
                      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                      .Build();

      XmlProj = XElement.Load(project);

      try
      {
        ReleaseTitle = XmlProj.Descendants("PackageReleaseTitle").First().Value;
        ReleaseNotes = XmlProj.Descendants("PackageReleaseNotes").First().Value;
        RepositoryUrl = XmlProj.Descendants("RepositoryUrl").First().Value;
        PackageId = XmlProj.Descendants("PackageId").First().Value;
        Version = XmlProj.Descendants("Version").First().Value;
        ReleaseTag = $"v{Version}";
      }
      catch (InvalidOperationException)
      {
        throw new InvalidOperationException("One of elements required for release preparation doesn't exist. See list of required elements in readme file of project");
      }

    }

    void IUpdateManager.CreateRelease()
    {
      string squirrel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Configuration["Settings:SquirrelPath"]);

      if (!File.Exists(squirrel))
        throw new FileNotFoundException($"'{squirrel}' not found");

      var package = Path.Combine(_path, @"bin\Release", $"{PackageId}.{Version}.nupkg");

      if (!File.Exists(package))
        throw new FileNotFoundException($"'{package}' file not found.");

      string errorMsg = "";

      using (var process = new Process())
      {
        process.StartInfo.FileName = squirrel;
        process.StartInfo.Arguments = $"{Configuration["Settings:SquirrelArgs"]} -r {_path}\\Releases --releasify {package}";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
        process.Start();
        errorMsg = process.StandardError.ReadToEnd();
        Console.WriteLine(process.StandardOutput.ReadToEnd());
      }

      if (!string.IsNullOrEmpty(errorMsg))
        throw new InvalidOperationException(errorMsg);
    }
    async Task IUpdateManager.UploadReleaseToGithub()
    {
      var client = new GitHubClient(new ProductHeaderValue("bdrum"));
      var tokenAuth = new Credentials("token");
      client.Credentials = tokenAuth;

      var newRelease = new NewRelease(ReleaseTag);
      newRelease.Name = ReleaseTitle;
      newRelease.Body = ReleaseNotes;

      var result = await client.Repository.Release.Create("bdrum", "octokit.net", newRelease);
      Console.WriteLine("Created release id {0}", result.Id);

      var latestRelease = client.Repository.Release.GetLatest("bdrum", "octokit.net");

      using (var archiveContents = File.OpenRead("output.nupkg"))
      {
        var assetUpload = new ReleaseAssetUpload()
        {
          FileName = "Nupkg",
          ContentType = "package",
          RawData = archiveContents
        };
        var asset = await client.Repository.Release.UploadAsset(latestRelease.Result, assetUpload);
      }
    }

    async Task IUpdateManager.UpdateCurrentProject()
    {

    }

  } //class UpdateManager
} //namespace Regata
