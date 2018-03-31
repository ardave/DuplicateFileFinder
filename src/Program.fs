open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography


let dupesFile = "dupes.txt"
let logToDupesFile str = File.AppendAllLines(dupesFile, [|str|])
let md5 = MD5.Create()
let sizes = Dictionary<Int64, FileInfo>()
let hashes = Dictionary<string, FileInfo>()
let mutable bytes = 0L
let mutable fileCount = 0

type Hash     = Hash     of string
type FileName = FileName of string

let getLength (fi:FileInfo) = fi.Length
let getFullName (fi:FileInfo) = FileName fi.FullName

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
        getHash fileInfo |> Ok
    with
        | :? IOException as ex -> Error ex

let inline stringf format (x : ^a) = 
    (^a : (member ToString : string -> string) (x, format))

let printProgress fileCount bytes =
    let megs = bytes / 1_000_000L
    printfn "Files processed: %i, megabytes processed: %s MB" fileCount (megs |> stringf "N0")

[<EntryPoint>]
let main _ =
    File.Delete dupesFile
    let allFiles = Directory.GetFiles("/Volumes/4gig", "*.*", SearchOption.AllDirectories)

    printfn "Found %i files." allFiles.Length
    let printStatus1 x =
        printfn "Found %i unique file sizes." (x |> Seq.length)
        x

    let printStatus2 x =
        printfn "Found %i unique file sizes matching more 1 file" (x |> Seq.length)
        x

    allFiles
    |> Seq.map FileInfo
    |> Seq.groupBy(fun fi -> fi.Length)
    |> printStatus1
    |> Seq.filter(fun gp -> gp |> snd |> Seq.length > 1)
    |> printStatus2
    |> Seq.sortByDescending fst
    |> Seq.iter(fun (_, fis) ->
        let hashTuples = fis |> Seq.map(fun fi -> fi |> tryGetHash, fi)
        
        let okays, errors = hashTuples |> Seq.map fst |> split |> Seq.zip (hashTuples |> Seq.map snd)


        let dupes =
            hashTuples
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
                logToDupesFile (sprintf "The following files all have size %s and hash %s" size (gp |> fst))

                gp
                |> snd
                |> Seq.iter (snd >> getFullName >> logToDupesFile)
                logToDupesFile ""
            )
        )

    printProgress fileCount bytes
    0
