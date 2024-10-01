use std::{collections::HashMap, env, fs::{File, OpenOptions}, io::{self, BufWriter, Read, Seek, SeekFrom, Write}, path::PathBuf};
use anyhow::Result;
use md5::Digest;
use walkdir::WalkDir;

fn main()-> Result<()> {
    let args: Vec<String> = env::args().collect();
    println!("Args looks like: {:?}", args);
    match args.as_slice() {
        [current_path ] => search_path_for_dupes(&current_path)?,
        | [_current, explicitly_requested] => search_path_for_dupes(explicitly_requested)?,
        _ => println!("You must include the folder path to scan.")
    }

    Ok(())
}

fn search_path_for_dupes(path: &str) -> Result<()> {
    let mut by_size: HashMap<u64, Vec<(u64, PathBuf)>> = HashMap::new();

    for entry in WalkDir::new(path) {
        match entry {
            Ok(dir_entry) => {
                if dir_entry.file_type().is_file() {
                    if let Ok(metadata) = dir_entry.metadata() {
                        let file_size = metadata.len();
                        let path_buf = dir_entry.path().to_path_buf();
                        if let Some(path_bufs) = by_size.get_mut(&file_size) {
                            path_bufs.push((file_size, path_buf));
                        } else {
                            by_size.insert(file_size, vec![(file_size, path_buf)]);
                        }                    
                    }                
                }
            },
            Err(e) => println!("WalkDir Error: {}", e)
        }
    }

    println!("Examined {} files.", by_size.values().map(|v| v.len()).sum::<usize>());
    by_size.retain(|_, value| value.len() > 1);
    println!("Duplicates by size: {}", by_size.values().map(|v| v.len()).sum::<usize>());

    let flattened: Vec<(u64, PathBuf)> = by_size
        .into_values()
        .flat_map(|v| v)
        .collect();

    let mut by_hash: HashMap<(Digest, u64), Vec<PathBuf>> = HashMap::new();
    for (file_size, path_buf) in flattened {
        let mut file = File::open(&path_buf)?;
        let mut buffer = vec![0; 1024];  // Initialize with a capacity of 1024 bytes
        let read_result = file.read(&mut buffer);

        match read_result {
            Ok(bytes_read) => {
                buffer.truncate(bytes_read);
                let digest = md5::compute(buffer);
                if let Some(path_bufs) = by_hash.get_mut(&(digest, file_size)) {
                    path_bufs.push(path_buf);
                } else {
                    by_hash.insert((digest,file_size), vec![path_buf]);
                }
                let hashed_count = by_hash.values().map(|v| v.len()).sum::<usize>();
                if hashed_count % 100 == 0 {
                    println!("{} files hashed.", hashed_count);
                }

            },
            Err(e) => println!("Hash file bytes error: {}", e),
        }
    }

    by_hash.retain(|_, value| value.len() > 1);
    
    // Convert HashMap into a Vec of key-value pairs
    let mut by_hash: Vec<_> = by_hash.into_iter().collect();

    // Sort the Vec by key in descending order
    by_hash.sort_by(|a, b| b.0.1.cmp(&a.0.1));


        // Open the file in write mode, overwriting it if it exists
    let file = OpenOptions::new()
        .write(true)       // Open for writing
        .create(true)      // Create the file if it doesn't exist
        .truncate(true)    // Overwrite the file if it exists
        .open("dupes.txt")?;

    let mut writer = BufWriter::new(file);

    println!("Dupes found:");
    writeln!(writer, "Dupes found:")?;
    for ((_digest, file_size), path_bufs) in by_hash {
        println!("Duplicates of size {}", file_size);
        writeln!(writer, "Duplicates of size {}", file_size)?;
        for path_buf in path_bufs {
            println!("\t{:?}", path_buf);
            writeln!(writer, "\t{:?}", path_buf)?;
        }
    }

    Ok(())
}

fn load_first_and_last_1024_bytes(filename: &str) -> io::Result<Vec<u8>> {
    let mut file = File::open(filename)?;

    // Get file size
    let file_size = file.metadata()?.len();

    // Prepare buffers for the first and last 1024 bytes
    let mut first_1024 = vec![0u8; 1024];
    let mut last_1024 = vec![0u8; 1024];

    // Read the first 1024 bytes (or less if the file is smaller)
    let first_bytes_read = file.read(&mut first_1024)?;

    // If the file is larger than 1024 bytes, seek to the last 1024 bytes
    if file_size > 1024 {
        let last_position = if file_size > 2048 {
            file_size - 1024
        } else {
            0
        };
        file.seek(SeekFrom::Start(last_position))?;
        let last_bytes_read = file.read(&mut last_1024)?;

        // Concatenate both buffers (truncate unused bytes)
        let mut combined_bytes = Vec::with_capacity(2048);
        combined_bytes.extend_from_slice(&first_1024[..first_bytes_read]);
        combined_bytes.extend_from_slice(&last_1024[..last_bytes_read]);
        Ok(combined_bytes)
    } else {
        // If the file is less than 1024 bytes, return only the part we could read
        Ok(first_1024[..first_bytes_read].to_vec())
    }
}
