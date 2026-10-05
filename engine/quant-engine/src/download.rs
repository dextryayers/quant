use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use tokio::io::AsyncWriteExt;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum JobState {
    Queued,
    Downloading,
    Verifying,
    Done,
    Error,
    Cancelled,
}

#[derive(Debug, Clone, Serialize)]
pub struct Job {
    pub job_id: String,
    pub hf_repo: String,
    pub filename: String,
    pub state: JobState,
    pub bytes: u64,
    pub total: Option<u64>,
    pub speed_bps: f64,
    pub message: String,
}

type Jobs = Arc<Mutex<HashMap<String, Job>>>;

#[derive(Clone)]
pub struct Downloader {
    jobs: Jobs,
    dir: PathBuf,
}

impl Downloader {
    pub fn new(dir: PathBuf) -> Self {
        Self { jobs: Arc::new(Mutex::new(HashMap::new())), dir }
    }

    pub fn start(&self, hf_repo: String, filename: String, sha256: Option<String>) -> String {
        let job_id = format!("dl-{}", rand_id());
        {
            let mut j = self.jobs.lock().unwrap();
            j.insert(job_id.clone(), Job {
                job_id: job_id.clone(),
                hf_repo: hf_repo.clone(),
                filename: filename.clone(),
                state: JobState::Queued,
                bytes: 0,
                total: None,
                speed_bps: 0.0,
                message: "queued".into(),
            });
        }
        let jobs = self.jobs.clone();
        let dir = self.dir.clone();
        let id_clone = job_id.clone();
        tokio::spawn(async move {
            if let Err(e) = run(&jobs, &id_clone, &dir, &hf_repo, &filename, sha256).await {
                let mut j = jobs.lock().unwrap();
                if let Some(job) = j.get_mut(&id_clone) {
                    job.state = JobState::Error;
                    job.message = e;
                }
            }
        });
        job_id
    }

    pub fn get(&self, id: &str) -> Option<Job> {
        self.jobs.lock().unwrap().get(id).cloned()
    }

    pub fn cancel(&self, id: &str) {
        if let Some(j) = self.jobs.lock().unwrap().get_mut(id) {
            j.state = JobState::Cancelled;
            j.message = "cancelled by user".into();
        }
    }
}

fn rand_id() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let n = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_nanos()).unwrap_or(1);
    format!("{n:x}")
}

async fn run(
    jobs: &Jobs,
    job_id: &str,
    dir: &Path,
    hf_repo: &str,
    filename: &str,
    sha256: Option<String>,
) -> Result<(), String> {
    let url = format!("https://huggingface.co/{hf_repo}/resolve/main/{filename}");
    let dest = dir.join(filename);
    let part = dir.join(format!("{filename}.part"));
    let _ = std::fs::create_dir_all(dir);
    let resume = std::fs::metadata(&part).map(|m| m.len()).unwrap_or(0);

    set(jobs, job_id, JobState::Downloading, resume, None, 0.0, format!("GET {url} resume={resume}"));
    let client = reqwest::Client::builder().timeout(std::time::Duration::from_secs(600)).build()
        .map_err(|e| e.to_string())?;
    let mut req = client.get(&url);
    if resume > 0 {
        req = req.header("Range", format!("bytes={resume}-"));
    }
    let mut resp = req.send().await.map_err(|e| format!("NETWORK: {e}"))?;
    if resp.status() == reqwest::StatusCode::NOT_FOUND {
        return Err(format!("NOT_FOUND: {url}"));
    }
    // If server ignores Range it returns 200, restart from zero.
    if resp.status().is_success() && resp.headers().get("content-range").is_none() && resume > 0 {
        let _ = std::fs::remove_file(&part);
    }
    let total = resp.content_length().map(|c| c + resume);
    let mut file = tokio::fs::OpenOptions::new()
        .create(true)
        .append(resume > 0)
        .write(true)
        .open(&part)
        .await
        .map_err(|e| e.to_string())?;
    // If fresh, truncate
    if resume == 0 {
        file.set_len(0).await.map_err(|e| e.to_string())?;
    }
    let t0 = std::time::Instant::now();
    let mut bytes = resume;
    let mut stream = resp.bytes_stream();
    use futures::StreamExt;
    while let Some(chunk) = stream.next().await {
        // cooperative cancel
        if let Some(j) = jobs.lock().unwrap().get(job_id) {
            if j.state == JobState::Cancelled {
                return Err("cancelled".into());
            }
        }
        let chunk = chunk.map_err(|e| format!("NETWORK: {e}"))?;
        file.write_all(&chunk).await.map_err(|e| e.to_string())?;
        bytes += chunk.len() as u64;
        let bps = bytes.saturating_sub(resume) as f64 / t0.elapsed().as_secs_f64().max(0.1);
        // throttle progress writes to every ~256KB
        if bytes % (256 * 1024) < chunk.len() as u64 {
            set(jobs, job_id, JobState::Downloading, bytes, total, bps, "downloading".into());
        }
        // retry/backoff handled by caller levels in V0.2, single pass here with clear error
    }
    file.flush().await.map_err(|e| e.to_string())?;
    drop(file);

    set(jobs, job_id, JobState::Verifying, bytes, total, 0.0, "verifying sha256".into());
    if let Some(expected) = sha256 {
        let digest = sha_file(&part).map_err(|e| e.to_string())?;
        if digest.to_lowercase() != expected.to_lowercase() {
            return Err(format!("SHA_MISMATCH: got {digest}"));
        }
    }
    std::fs::rename(&part, &dest).map_err(|e| e.to_string())?;
    set(jobs, job_id, JobState::Done, bytes, total, 0.0, format!("saved {}", dest.to_string_lossy()));
    Ok(())
}

fn set(jobs: &Jobs, id: &str, state: JobState, bytes: u64, total: Option<u64>, bps: f64, msg: String) {
    if let Some(j) = jobs.lock().unwrap().get_mut(id) {
        j.state = state;
        j.bytes = bytes;
        j.total = total;
        j.speed_bps = bps;
        j.message = msg;
    }
}

fn sha_file(path: &Path) -> std::io::Result<String> {
    let mut f = std::fs::File::open(path)?;
    let mut h = Sha256::new();
    std::io::copy(&mut f, &mut h)?;
    Ok(format!("{:x}", h.finalize()))
}

#[cfg(test)]
mod tests {
    #[test]
    fn url_shape() {
        let u = format!("https://huggingface.co/{}/{}/resolve/main/{}", "a", "b", "c");
        assert!(u.contains("huggingface.co"));
    }
}
