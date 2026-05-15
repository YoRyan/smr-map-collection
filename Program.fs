open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

open FsToolkit.ErrorHandling
open SharpCompress.Archives
open SharpCompress.Archives.Zip
open SharpCompress.Archives.SevenZip

type MapJson =
    { pid: string
      label: string
      version: string option
      created: string option
      updated: string option
      author: string option
      modified_by: string option
      ``type``: string option
      description: string }

type private Map =
    { Json: MapJson
      PreviewJpeg: byte array option }

let private split2 (sep: string) (s: string) =
    match s.Split(sep, 2, StringSplitOptions.RemoveEmptyEntries) with
    | [||] -> "", ""
    | [| s1 |] -> s1, ""
    | [| s1; s2 |] -> s1, s2
    | _ -> failwith "Should Never Happen"

let private tryGetByref<'T> =
    function
    | (true, v: 'T) -> Some v
    | false, _ -> None

let private tryGetValue<'K, 'V> (d: IDictionary<'K, 'V>) (key: 'K) = d.TryGetValue key |> tryGetByref

let private readMapInfo (archive: IArchive) =
    result {
        let! entry =
            archive.Entries
            |> Seq.filter (fun e -> Regex.IsMatch(e.Key, @"(^|/)mapInfo\.txt(/|$)", RegexOptions.IgnoreCase))
            |> Seq.tryHead
            |> function
                | Some e -> Ok e
                | None -> Error "Failed to locate mapInfo.txt in this archive"

        use stream = entry.OpenEntryStream()
        use reader = new StreamReader(stream)

        let text = reader.ReadToEnd() |> _.ReplaceLineEndings("\n")

        let header, description = split2 "\n\n" text

        let fields =
            header
            |> _.Split("\n")
            |> Seq.filter (fun line -> line <> "")
            |> Seq.map (fun line ->
                let name, value = split2 ":" line

                let nameNorm =
                    name
                    |> _.Trim()
                    |> _.Split(" ", StringSplitOptions.RemoveEmptyEntries)
                    |> List.ofArray
                    |> List.map _.ToLowerInvariant()

                nameNorm, value.Trim())
            |> dict

        return fields, description.Trim()
    }

let private readMapPreview (archive: IArchive) =
    option {
        let! entry =
            archive.Entries
            |> Seq.filter (fun e -> Regex.IsMatch(e.Key, @"(^|/)mapIcon\.jpe?g(/|$)", RegexOptions.IgnoreCase))
            |> Seq.tryHead

        use stream = entry.OpenEntryStream()
        use ms = new MemoryStream()
        stream.CopyTo ms
        return ms.ToArray()
    }

let private tryParseType (s: string) =
    let s = s.ToLowerInvariant()

    if s.Contains "single" then Some "single"
    elif s.Contains "multi" then Some "multi"
    else None

let private readMapArchive (stream: Stream) (fileName: string) =
    result {
        use archive = SevenZipArchive.OpenArchive stream
        let! fields, description = readMapInfo archive

        return
            { Json =
                { pid = fileName
                  label = tryGetValue fields [ "map"; "name" ] |> Option.defaultValue fileName
                  version = tryGetValue fields [ "map"; "version" ]
                  created = tryGetValue fields [ "date"; "created" ]
                  updated = tryGetValue fields [ "date"; "updated" ]
                  author = tryGetValue fields [ "author" ]
                  modified_by = tryGetValue fields [ "modified"; "by" ]
                  ``type`` = tryGetValue fields [ "type" ] |> Option.bind tryParseType
                  description = description }
              PreviewJpeg = readMapPreview archive }
    }

let private readZipDownloadOfMaps (stream: Stream) =
    use archive = ZipArchive.OpenArchive stream

    archive.Entries
    |> Seq.filter (fun e -> Regex.IsMatch(e.Key, @"\.7z$", RegexOptions.IgnoreCase))
    |> Seq.choose (fun e ->
        use stream = e.OpenEntryStream()
        // Any stream we read as an archive must be seekable.
        use ms = new MemoryStream()
        stream.CopyTo ms

        match readMapArchive ms e.Key with
        | Ok map -> Some map
        | Error err ->
            printfn "Error processing %s: %s" e.Key err
            None)

let private doProcessZip (zipPath: FileInfo) (outPath: DirectoryInfo) =
    use inStream = zipPath.OpenRead()

    let rawImages = Path.Combine(string outPath, "raw_images", "smr")
    Directory.CreateDirectory rawImages |> ignore

    let mutable json = []

    for map in readZipDownloadOfMaps inStream do
        match map.PreviewJpeg with
        | Some bytes ->
            let imagePath = Path.Combine(rawImages, $"{map.Json.pid}.jpeg")
            File.WriteAllBytes(imagePath, bytes)
        | None -> ()

        json <- map.Json :: json

    use jsonStream =
        File.Open(Path.Combine(string outPath, "smr.json"), FileMode.Create)

    json <- List.rev json
    JsonSerializer.Serialize(jsonStream, json)

[<EntryPoint>]
let main argv =
    let root =
        CommandLine.RootCommand "Generate Wax metadata for the Sid Meier's Railroads Custom Maps Collection."

    let outPath = new CommandLine.Option<DirectoryInfo>("--outPath", "-o")
    outPath.Description <- "Directory path to write Wax metadata to."
    outPath.Required <- true
    root.Options.Add outPath

    let readZip =
        CommandLine.Command(
            "read-zip",
            "Read maps from a complete Zip download of the collection from the Internet Archive."
        )

    let readZipIn = new CommandLine.Argument<FileInfo> "zipPath"
    readZipIn.Description <- "Path to the downloaded Zip file."
    readZip.Add readZipIn

    readZip.SetAction(fun result ->
        let zipPath = result.GetRequiredValue readZipIn
        let outPath = result.GetRequiredValue outPath
        doProcessZip zipPath outPath)

    root.Subcommands.Add readZip

    (root.Parse argv).Invoke()
