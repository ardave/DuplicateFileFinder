open System
open System.IO
open System.Security.Cryptography

type Hash     = Hash     of string
type FileName = FileName of string
let dupesFile  = "dupes.txt"
let errorsFile = "errors.txt"
let logToDupesFile str = File.AppendAllLines(dupesFile, [|str|])
let md5 = MD5.Create()
let mutable bytes = 0L
let mutable fileCount = 0

let getLength (fi:FileInfo) = fi.Length
let getFullName (fi:FileInfo) = FileName fi.FullName
let unpackFileName (FileName str) = str

let split resultSequence =
    let oList, eList =
        resultSequence
        |> Seq.fold(fun (os, es) elem ->
            match elem with
            | Ok o -> o::os, es
            | Error e -> os, e::es
        ) ([],[])
    let prepare lst = lst |> List.rev |> Seq.ofList
    prepare oList, prepare eList

let getHash (fileInfo:FileInfo) =
    let toStr hashArray =  BitConverter.ToString(hashArray).Replace("-", "").ToLowerInvariant()

    File.OpenRead(fileInfo.FullName)
    |> md5.ComputeHash
    |> toStr
    |> Hash

let tryGetHash fileInfo =
    try
        let hash = getHash fileInfo
        Ok(hash, fileInfo)
    with
        | :? IOException as ex -> Error(ex, fileInfo)

let inline stringf format (x : ^a) = 
    (^a : (member ToString : string -> string) (x, format))

let printProgress fileCount bytes =
    let megs = bytes / 1_000_000L
    printfn "Files processed: %i, megabytes processed: %s MB" fileCount (megs |> stringf "N0")

let printStatus1 x =
    printfn "Found %i unique file sizes." (x |> Seq.length)
    x

let printStatus2 x =
    printfn "Found %i unique file sizes matching more than 1 file" (x |> Seq.length)
    x

[<EntryPoint>]
let main _ =
    File.Delete dupesFile
    let allFiles = Directory.GetFiles("/Volumes/4gig", "*.*", SearchOption.AllDirectories)

    printfn "Found %i files." allFiles.Length

    allFiles
    |> Seq.map FileInfo
    |> Seq.groupBy(fun fi -> fi.Length)
    |> printStatus1
    |> Seq.filter(fun gp -> gp |> snd |> Seq.length > 1)
    |> printStatus2
    |> Seq.sortByDescending fst
    |> Seq.iter(fun (_, fis) ->
        let hashTuples = fis |> Seq.map tryGetHash

        let okays, errors = hashTuples |> split 


        let dupes =
            okays
            |> Seq.groupBy fst
            |> Seq.filter(fun gp -> gp |> snd |> Seq.length > 1)

        dupes
        |> Seq.iter(fun gp ->
                let size =
                    gp
                    |> snd
                    |> Seq.head
                    |> snd
                    |> getLength
                    |> stringf "N0"
                let (Hash hashstr) = fst gp
                logToDupesFile (sprintf "The following files all have size %s and hash %s" size hashstr)

                gp
                |> snd
                |> Seq.iter (snd >> getFullName >> unpackFileName >> logToDupesFile)
                logToDupesFile ""
            )
        )

    printProgress fileCount bytes
    0
