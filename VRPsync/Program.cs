// VRPSync by TheRadziu
// 2023
// v1.2.2
//todo: fix rouge new line between copying/downloading XXX and COPY/DOWNLOAD COMPLETED + after Proxy is found and enabled (same issue in rclone_transfer)
//todo: handle when config doesnt have all setting lines - Set them to null before foreach?

using System.Diagnostics;
using System.Security.Cryptography;
using System.Globalization;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Data;

class Config
{
    public static string rclonePath { get; private set; }
    public static string rcloneConfigPath { get; private set; }
    public static string sevenzipPath { get; private set; }
    public static string tempPath { get; private set; }
    public static string rcloneDestinationDir { get; private set; }
    public static string proxy { get; private set; }

    private static readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
    private static readonly Dictionary<string, string> _defaultSettings = new Dictionary<string, string>()
    {
        {"# rclonePath can be either C:\\rclone.exe for windows or /bin/rclone for linux\nrclonePath", "full_rclone_binary_path_or_command_here"},
        {"# if set, it'll use that as custom config path\nrcloneConfigPath", ""},
        {"# sevenzipPath can be either C:/7z.exe for windows or /bin/7z for linux\nsevenzipPath", "full_7zip_binary_path_or_command_here"},
        {"# tempPath is a directory where all temp files will be handled in. Has to be valid directory path\ntempPath", "full_temp_directory_path_here"},
        {"# rcloneDestinationDir has to be valid rclone destination for example my_ftp:, can be subdirectory too, like my_ftp:/backups\nrcloneDestinationDir", "rclone_destination_here"},
        {"# if set, http proxy will be enabled for all download actions, it can be either http(s)://user:password@ip:port or http(s)://ip:port for no auth\nproxy", ""},
    };

    public static void LoadSettings()
    {
        if (!File.Exists(configPath))
        {
            using (StreamWriter writer = new StreamWriter(configPath))
            {
                foreach (var setting in _defaultSettings)
                {
                    writer.WriteLine("{0} = {1}", setting.Key, setting.Value);
                }
            }
            Console.WriteLine("ATTENTION! 'config.ini' file was missing so it was created. Please edit it with proper settings and restart this app.");
            VRPSync.exit(0);
        }

        string[] lines = File.ReadAllLines(configPath);
        foreach (string line in lines)
        {
            if (!line.Trim().StartsWith("#"))
            {
                string[] parts = line.Split('=');
                string key = parts[0].Trim();
                string value = parts[1].Trim();

                switch (key)
                {
                    case "rclonePath":
                        rclonePath = value;
                        if (!File.Exists(rclonePath) && !Directory.Exists(rclonePath))
                        {
                            Console.WriteLine("Error: rclone not found or specified command is not valid.");
                            VRPSync.exit(-1);
                        }
                        break;
                    case "rcloneConfigPath":
                        rcloneConfigPath = value;
                        if (!File.Exists(rcloneConfigPath) && !Directory.Exists(rcloneConfigPath))
                        {
                            Console.WriteLine("Error: rclone config not found.");
                            VRPSync.exit(-1);
                        }
                        break;
                    case "sevenzipPath":
                        sevenzipPath = value;
                        if (!File.Exists(sevenzipPath) && !Directory.Exists(sevenzipPath))
                        {
                            Console.WriteLine("Error: 7zip not found or specified command is not valid.");
                            VRPSync.exit(-1);
                        }
                        break;
                    case "tempPath":
                        tempPath = value;
                        if (!Directory.Exists(tempPath))
                        {
                            Console.WriteLine("Error: TempPath not found or not valid directory.");
                            VRPSync.exit(-1);
                        }
                        break;
                    case "rcloneDestinationDir":
                        rcloneDestinationDir = value;
                        //todo: check if destination rclone is valid or is valid local path
                        break;
                    case "proxy":
                        proxy = value;
                        if (value != "")
                        {
                            Console.WriteLine("!! PROXY IS FOUND AND ENABLED !!");
                            //todo: check if proxy format matches http(s)://(username:pass@)ip:port
                        }
                        break;
                    default:
                        // Handle unexpected key
                        break;
                }
            }
        }
    }
}

class VRPconfig
{
    public static string server;
    public static string password;
    public static void LoadConfig()
    {
        try
        {
            WebClient client = new WebClient();
            if (Config.proxy != "")
            {
                string proxyUrl = Config.proxy;
                if (proxyUrl.Contains("@"))
                {
                    WebProxy proxy = new WebProxy(proxyUrl);
                    string[] creds = proxyUrl.Split(new[] { ':' }, 3);
                    proxy.Credentials = new NetworkCredential(creds[1].Substring(2), creds[2].Substring(0, creds[2].IndexOf("@")));
                    client.Proxy = proxy;
                }
                else
                {
                    client.Proxy = new WebProxy(proxyUrl);
                }
            }
            string json = client.DownloadString("https://wiki.vrpirates.club/downloads/vrp-public.json");
            JObject config = JObject.Parse(json);
            server = (string)config["baseUri"];
            string encodedPassword = (string)config["password"];
            byte[] data = Convert.FromBase64String(encodedPassword);
            password = Encoding.UTF8.GetString(data);
        }
        catch (WebException e)
        {
            if (File.Exists("vrp-public.json"))
            {
                string json = File.ReadAllText("vrp-public.json");
                JObject config = JObject.Parse(json);
                server = (string)config["baseUri"];
                string encodedPassword = (string)config["password"];
                byte[] data = Convert.FromBase64String(encodedPassword);
                password = Encoding.UTF8.GetString(data);
            }
            else
            {
                Console.WriteLine("Error loading config file: " + e.Message);
                VRPSync.exit(-1);
            }
        }
    }
}

class VRPSync
{
    internal static string[] RcloneDirList()
    {
        string args = String.Empty;
        var dirList = new List<string>();
        args = $"lsf \"{Config.rcloneDestinationDir}\"";
        if (Config.rcloneConfigPath != "")
            args += $" --config=\"{Config.rcloneConfigPath}\"";
        var process = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = Config.rclonePath,
                Arguments = args,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        process.Start();
        while (!process.StandardOutput.EndOfStream)
        {
            string line = process.StandardOutput.ReadLine();
            dirList.Add(line.Remove(line.Length-1));
        }
        return dirList.ToArray();
    }

    public static void exit(int code)
    {
        Console.WriteLine("Press any button to exit VRPsync.");
        Console.CursorVisible = true;
        Console.ReadKey();
        Environment.Exit(code);
    }

    public static void remove_current_line()
    {
        int currentLineCursor = Console.CursorTop;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(new string(' ', Console.WindowWidth));
        Console.SetCursorPosition(0, currentLineCursor);
    }

    public static void rclone_transfer(string? hash, string? title)
    {
        int debug = 0;
        string args = String.Empty;
        if (title is not null)
        {
            string fullPath = string.Format("{0}{1}{2}", Config.tempPath, Path.DirectorySeparatorChar, title);
            args = $"copy \"{fullPath}\" \"{Config.rcloneDestinationDir}/{title}\" --fast-list --drive-chunk-size 32M --rc";
            if (Config.rcloneConfigPath != "")
                args += $" --config=\"{Config.rcloneConfigPath}\"";
        }
        else
        {
            args = $"copy --http-url {VRPconfig.server} \":http:/{hash}\" \"{Config.tempPath}\" --rc --tpslimit 1.0 --tpslimit-burst 3";
        }
        if (debug == 1)
        {
            args += $" --log-file={Config.tempPath}/log.txt --log-level DEBUG";
        }
        var rcloneProcess = new Process
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = Config.rclonePath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        List<string> proxyEnvVars = new List<string>() { "http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY" };
        foreach (string envVar in proxyEnvVars)
        {
            if (Config.proxy != "" & title == null)
                rcloneProcess.StartInfo.EnvironmentVariables[envVar] = Config.proxy;
            else
            {
                rcloneProcess.StartInfo.EnvironmentVariables[envVar] = "";
            }
        }

        try
        {
            rcloneProcess.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: rclone process couldnt be started: " + ex.Message);
        }

        System.Threading.Thread.Sleep(1000);

        while (!rcloneProcess.HasExited)
        {
            try
            {
                var client = new WebClient();
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                var jsonCommand = new { command = "transfers" };
                string jsonCommandString = JsonConvert.SerializeObject(jsonCommand);
                var json = client.UploadString("http://localhost:5572/core/stats", "POST", jsonCommandString);
                var stats = JObject.Parse(json);

                if (stats["transferring"] != null && stats["transferring"][0] != null)
                {
                    var bytes = (double)stats["bytes"];
                    var totalBytes = (double)stats["totalBytes"];
                    var speed = (double)stats["speed"];
                    int? ETA = (int?)stats["eta"];
                    var percentage = (bytes / totalBytes) * 100;
                    var percentFormatted = percentage.ToString("F2", CultureInfo.InvariantCulture);
                    var speedMBs = speed / (1024 * 1024);
                    string ETAFormatted = String.Empty;
                    if (ETA.HasValue)
                    {
                        TimeSpan t = TimeSpan.FromSeconds(ETA.Value);
                        ETAFormatted = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
                    }
                    else
                    {
                        ETAFormatted = "--:--";
                    }
                    Console.Write("\rProgress: " + percentFormatted + "%    Speed: " + speedMBs.ToString("F2", CultureInfo.InvariantCulture) + " MB/s    ETA: " + ETAFormatted + " ");
                }
                System.Threading.Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                //do nothing
            }
        }
        rcloneProcess.WaitForExit();
        if (rcloneProcess.ExitCode == 0)
        {
            remove_current_line();
            if (title != null)
            {
                Console.Write("COPY COMPLETED.\n");
            }
            else if(hash == "meta.7z")
            {
                Console.Write("Downloaded latest VRP GameList, it's last modification date is: ");
            }
            else
            {
                Console.Write("DOWNLOAD COMPLETED.\n");
            };
        }
        else
        {
            remove_current_line();
            if (hash == "meta.7z")
            {
                Console.Write("Failed to download latest GameList. VRP server might be down!\n");
            }
            else if (title != null)
            {
                Console.Write("Failed to upload " + title + "Your config might be corrupted!\n");
            }
            else
                Console.Write("Failed to download " + hash + ". VRP server might be down!\n");
            exit(-1);
        }
    }

    public static void rclone_remove(string title)
    {
        string args = String.Empty;
        args = $"purge \"{Config.rcloneDestinationDir}/{title}\"";
        if (Config.rcloneConfigPath != "")
            args += $" --config=\"{Config.rcloneConfigPath}\"";
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Config.rclonePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }

    public static void remove_all(string file)
    {
        string[] files = Directory.GetFiles(Config.tempPath);
        foreach (string fullpath in files)
        {
            if (Path.GetFileName(fullpath).StartsWith(file))
            {
                File.Delete(fullpath);
            }
        }
    }

    public static string calc_md5(string release_name)
    {
        using (var md5 = MD5.Create())
        {
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(release_name + "\n");
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString().ToLower();
        }
    }

    public static void ExtractFilesFrom7z(string archiveFile, string[] filesToExtract = null, string password = null)
    {
        try
        {
            string fullPath = string.Format("{0}{1}{2}", Config.tempPath, Path.DirectorySeparatorChar, archiveFile);
            string arguments = string.Format("x -y -o{0} \"{1}\"", Config.tempPath, fullPath);
            if (password != null)
            {
                arguments += string.Format(" -p{0}", password);
            }

            if (filesToExtract != null)
            {
                arguments += " " + string.Join(" ", filesToExtract);
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Config.sevenzipPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                Console.WriteLine("Error: " + output);
            }
            else if (archiveFile != "meta.7z")
            {
                Console.WriteLine("EXTRACTION COMPLETED.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }

    static DataTable LoadCSV(string file, char separator)
    {
            DataTable dt = new DataTable();

            using (StreamReader sr = new StreamReader(string.Format("{0}{1}{2}", Config.tempPath, Path.DirectorySeparatorChar, file)))
            {
                string[] headers = sr.ReadLine().Split(separator);
                foreach (string header in headers)
                {
                    dt.Columns.Add(header);
                }
                while (!sr.EndOfStream)
                {
                    string[] rows = sr.ReadLine().Split(separator);
                    DataRow dr = dt.NewRow();
                    for (int i = 0; i < headers.Length; i++)
                    {
                        dr[i] = rows[i];
                    }
                    dt.Rows.Add(dr);
                }

            }
            return dt;
    }

public static void Main()
    {
        Console.CursorVisible = false; //0. disable blinking thingy.
        Config.LoadSettings(); //1. Load local settings from config.ini file
        VRPconfig.LoadConfig(); //2. Download latest vrp config file and load it
        rclone_transfer("meta.7z", null); //3. Download latest meta.7z
        ExtractFilesFrom7z("meta.7z", new string[] { "VRP-GameList.txt" }, VRPconfig.password); //4. Extract gamelist from meta.7z
        Console.WriteLine(new FileInfo(string.Format("{0}{1}{2}", Config.tempPath, Path.DirectorySeparatorChar, "VRP-GameList.txt")).LastWriteTime.ToString("yyyy.MM.dd HH:mm:ss")); //5. print last modification date
        File.Delete(string.Format("{0}{1}{2}", Config.tempPath, Path.DirectorySeparatorChar, "meta.7z")); //6. Delete meta.7z, we dont need it at this point)
        DataTable gamelist = LoadCSV("VRP-GameList.txt", ';'); //7. Load and parse gamelist
        string[] current_rclone_list = RcloneDirList(); //8. Make a list of already downloaded games
        int new_titles = 0;
        int old_titles = 0;
        foreach (DataRow row in gamelist.Rows)
        {
            if (row["Release Name"] != null && !current_rclone_list.Contains(row["Release Name"])) //9. if release hasn't been downloaded yet:
            {
                new_titles += 1;
                Console.WriteLine("Downloading "+row["Release Name"]);
                string ReleasenameMD5 = calc_md5(row["Release Name"].ToString()); //calculate MD5 from release name
                rclone_transfer(ReleasenameMD5, null); //Download the release
                Console.WriteLine("Extracting " + row["Release Name"]);
                ExtractFilesFrom7z(ReleasenameMD5+".7z.001", null, VRPconfig.password); //Extract the release from first part
                remove_all(ReleasenameMD5); //Delete 7z files
                Console.WriteLine("Copying " + row["Release Name"]);
                rclone_transfer(null, row["Release Name"].ToString()); //Copy the extracted directory
                Directory.Delete(string.Format("{0}{1}{2}", Config.tempPath, Path.DirectorySeparatorChar, row["Release Name"]), true); //Delete the directory from tempdir
            }
        }
        foreach (string release_name_dir in current_rclone_list)
        {
            if (!gamelist.AsEnumerable().Any(row => row.Field<string>("Release Name") == release_name_dir))
            {
                old_titles += 1;
                Console.WriteLine("THIS IS NOT ON GAMELIST, REMOVING: " + release_name_dir);
                rclone_remove(release_name_dir);
            }
        }
        File.Move(string.Format("{0}{1}{2}", Config.tempPath, Path.DirectorySeparatorChar, "VRP-GameList.txt"), string.Format("{0}{1}{2}", AppDomain.CurrentDomain.BaseDirectory, Path.DirectorySeparatorChar, "VRP-GameList.txt"), true);
        if (new_titles != 0 || old_titles != 0)
        {
            Console.WriteLine(string.Format("New Titles: {0} | Removed Titles: {1}", new_titles, old_titles));
        }
        else
        {
            Console.WriteLine("Nothing has changed and your copy is up to date!");
        }
        exit(0);
        //Download: rclone_transfer(calc_md5(input), null);
        //Upload: rclone_transfer(null, input);
        //remove Local files: remove_all(calc_md5(input));
        //remove rclone path: rclone_remove(input);
        //extract: ExtractFilesFrom7z("meta.7z", new string[] { "VRP-GameList.txt" }, VRPconfig.password);
    }
}
