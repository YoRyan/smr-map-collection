open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

open FsToolkit.ErrorHandling
open SharpCompress.Archives
open SharpCompress.Archives.Zip
open SharpCompress.Archives.SevenZip

type Map =
    { MapName: string
      MapVersion: string option
      DateCreated: DateTime option
      DateUpdated: DateTime option
      Author: string option
      ModifiedBy: string option
      Type: string option
      Description: string
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

let private tryParseDateTime (s: string) = DateTime.TryParse s |> tryGetByref

let private readMapArchive (stream: Stream) =
    result {
        use archive = SevenZipArchive.OpenArchive stream
        let! fields, description = readMapInfo archive
        let preview = readMapPreview archive

        let! name =
            tryGetValue fields [ "map"; "name" ]
            |> function
                | Some s -> Ok s
                | None -> Error "This mapInfo.txt does not define a map name"

        return
            { MapName = name
              MapVersion = tryGetValue fields [ "map"; "version" ]
              DateCreated = tryGetValue fields [ "date"; "created" ] |> Option.bind tryParseDateTime
              DateUpdated = tryGetValue fields [ "date"; "updated" ] |> Option.bind tryParseDateTime
              Author = tryGetValue fields [ "author" ]
              ModifiedBy = tryGetValue fields [ "modified"; "by" ]
              Type = tryGetValue fields [ "type" ]
              Description = description
              PreviewJpeg = preview }
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

        match readMapArchive ms with
        | Ok map -> Some map
        | Error err ->
            printfn "Error processing %s: %s" e.Key err
            None)

let private doReadZip (zipPath: FileInfo) (jsonPath: FileInfo) =
    use inStream = zipPath.OpenRead()
    let maps = readZipDownloadOfMaps inStream
    use outStream = jsonPath.OpenWrite()
    JsonSerializer.Serialize(outStream, maps)

[<EntryPoint>]
let main argv =
    let root =
        CommandLine.RootCommand "Generate JSON metadata for the Sid Meier's Railroads Custom Maps Collection."

    let readZip =
        CommandLine.Command(
            "read-zip",
            "Read maps from a complete Zip download of the collection from the Internet Archive."
        )

    let readZipIn = new CommandLine.Argument<FileInfo> "zipPath"
    readZipIn.Description <- "Path to the downloaded Zip file."
    readZip.Add readZipIn
    let readZipOut = new CommandLine.Argument<FileInfo> "jsonPath"
    readZipOut.Description <- "Path to write the JSON metadata to."
    readZip.Add readZipOut

    readZip.SetAction(fun result ->
        let zipPath = result.GetRequiredValue readZipIn
        let jsonPath = result.GetRequiredValue readZipOut
        doReadZip zipPath jsonPath)

    root.Subcommands.Add readZip

    (root.Parse argv).Invoke()
