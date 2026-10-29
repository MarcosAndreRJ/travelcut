//! Asynchronous priority-based job queue and concurrency manager.
//!
//! Controls concurrent process execution via semaphores, prioritizes interactive
//! tasks over background tasks, provides cooperative cancellation, and broadcasts
//! real-time events to the UI.

use crate::error::{MediaEngineError, Result};
use crate::models::{JobEvent, JobId, JobPriority, JobProgress, JobStatus, JobType};
use crate::queue::job::TrackedJob;
use futures::future::BoxFuture;
use std::collections::{BinaryHeap, HashMap};
use std::sync::atomic::Ordering;
use std::sync::Arc;
use std::time::Instant;
use tokio::sync::{broadcast, mpsc, Mutex, RwLock, Semaphore};
use uuid::Uuid;

type JobTask = Box<dyn FnOnce(Arc<std::sync::atomic::AtomicBool>, mpsc::Sender<JobProgress>) -> BoxFuture<'static, Result<()>> + Send + 'static>;

struct PrioritizedItem {
    priority: JobPriority,
    sequence: u64,
    job_id: JobId,
    job_type: JobType,
    task: JobTask,
}

impl PartialEq for PrioritizedItem {
    fn eq(&self, other: &Self) -> bool {
        self.priority == other.priority && self.sequence == other.sequence
    }
}

impl Eq for PrioritizedItem {}

impl PartialOrd for PrioritizedItem {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for PrioritizedItem {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        // Highest priority first; if equal, lower sequence (older) first
        self.priority
            .cmp(&other.priority)
            .then_with(|| other.sequence.cmp(&self.sequence))
    }
}

/// Central manager orchestrating media engine jobs.
#[derive(Clone)]
pub struct JobQueueManager {
    semaphore: Arc<Semaphore>,
    tracked_jobs: Arc<RwLock<HashMap<JobId, TrackedJob>>>,
    event_tx: broadcast::Sender<JobEvent>,
    queue_tx: mpsc::Sender<()>,
    pending_heap: Arc<Mutex<BinaryHeap<PrioritizedItem>>>,
    sequence_counter: Arc<std::sync::atomic::AtomicU64>,
}

impl JobQueueManager {
    /// Create a new queue manager with a maximum concurrent task limit.
    pub fn new(max_concurrency: usize) -> Self {
        let concurrency = max_concurrency.clamp(1, 16);
        let semaphore = Arc::new(Semaphore::new(concurrency));
        let tracked_jobs = Arc::new(RwLock::new(HashMap::new()));
        let (event_tx, _) = broadcast::channel(1024);
        let (queue_tx, mut queue_rx) = mpsc::channel::<()>(1024);
        let pending_heap = Arc::new(Mutex::new(BinaryHeap::new()));
        let sequence_counter = Arc::new(std::sync::atomic::AtomicU64::new(0));

        let mgr = Self {
            semaphore: semaphore.clone(),
            tracked_jobs: tracked_jobs.clone(),
            event_tx: event_tx.clone(),
            queue_tx,
            pending_heap: pending_heap.clone(),
            sequence_counter,
        };

        // Spawn background worker scheduler
        let tracked_clone = tracked_jobs.clone();
        let heap_clone = pending_heap.clone();
        let sem_clone = semaphore.clone();
        let event_clone = event_tx.clone();

        tokio::spawn(async move {
            while queue_rx.recv().await.is_some() {
                // Drain any items that can be dispatched
                loop {
                    // Check if we have available permits
                    let permit = match sem_clone.clone().try_acquire_owned() {
                        Ok(p) => p,
                        Err(_) => break, // All concurrency slots are currently occupied
                    };

                    let next_item = {
                        let mut heap = heap_clone.lock().await;
                        heap.pop()
                    };

                    let item = match next_item {
                        Some(it) => it,
                        None => {
                            // No pending items in heap, release permit
                            drop(permit);
                            break;
                        }
                    };

                    let job_id = item.job_id.clone();
                    let job_type = item.job_type;
                    let tracked = {
                        let jobs = tracked_clone.read().await;
                        jobs.get(&job_id).cloned()
                    };

                    let tracked_job = match tracked {
                        Some(t) => t,
                        None => {
                            drop(permit);
                            continue;
                        }
                    };

                    // Check if job was cancelled before starting
                    if tracked_job.cancel_flag.load(Ordering::Relaxed) {
                        *tracked_job.status.write().await = JobStatus::Cancelled;
                        let _ = event_clone.send(JobEvent::Cancelled {
                            job_id: job_id.clone(),
                        });
                        drop(permit);
                        continue;
                    }

                    // Update status to Running
                    *tracked_job.status.write().await = JobStatus::Running;
                    *tracked_job.started_at.write().await = Some(Instant::now());
                    let _ = event_clone.send(JobEvent::Started {
                        job_id: job_id.clone(),
                        job_type,
                    });

                    let (prog_tx, mut prog_rx) = mpsc::channel::<JobProgress>(64);
                    let event_tx_sub = event_clone.clone();
                    let tracked_sub = tracked_job.clone();

                    // Progress relay task
                    tokio::spawn(async move {
                        while let Some(prog) = prog_rx.recv().await {
                            *tracked_sub.progress.write().await = Some(prog.clone());
                            let _ = event_tx_sub.send(JobEvent::Progress(prog));
                        }
                    });

                    // Execute task
                    let event_tx_finish = event_clone.clone();
                    let tracked_finish = tracked_job.clone();
                    let cancel_flag = tracked_job.cancel_flag.clone();
                    let task = item.task;

                    tokio::spawn(async move {
                        let _permit = permit; // Holds permit until future completes
                        let start_time = Instant::now();
                        let result = task(cancel_flag.clone(), prog_tx).await;
                        *tracked_finish.finished_at.write().await = Some(Instant::now());
                        let duration = start_time.elapsed().as_secs_f64();

                        match result {
                            Ok(()) => {
                                *tracked_finish.status.write().await = JobStatus::Completed;
                                let _ = event_tx_finish.send(JobEvent::Completed {
                                    job_id,
                                    job_type,
                                    duration_secs: duration,
                                });
                            }
                            Err(MediaEngineError::TaskCancelled { .. }) => {
                                *tracked_finish.status.write().await = JobStatus::Cancelled;
                                let _ = event_tx_finish.send(JobEvent::Cancelled { job_id });
                            }
                            Err(e) => {
                                if cancel_flag.load(Ordering::Relaxed) {
                                    *tracked_finish.status.write().await = JobStatus::Cancelled;
                                    let _ = event_tx_finish.send(JobEvent::Cancelled { job_id });
                                } else {
                                    let err_msg = e.to_string();
                                    *tracked_finish.error_message.write().await = Some(err_msg.clone());
                                    *tracked_finish.status.write().await = JobStatus::Failed;
                                    let _ = event_tx_finish.send(JobEvent::Failed {
                                        job_id,
                                        job_type,
                                        error: err_msg,
                                    });
                                }
                            }
                        }
                    });
                }
            }
        });

        mgr
    }

    /// Subscribe to real-time job events for UI and telemetry.
    pub fn subscribe(&self) -> broadcast::Receiver<JobEvent> {
        self.event_tx.subscribe()
    }

    /// Return the number of currently available execution permits.
    pub fn available_permits(&self) -> usize {
        self.semaphore.available_permits()
    }

    /// Submit a task to the queue with specified priority.
    pub async fn submit<F, Fut>(&self, job_type: JobType, priority: JobPriority, task_fn: F) -> JobId
    where
        F: FnOnce(Arc<std::sync::atomic::AtomicBool>, mpsc::Sender<JobProgress>) -> Fut + Send + 'static,
        Fut: std::future::Future<Output = Result<()>> + Send + 'static,
    {
        let job_id = format!("{}_{}", job_type, Uuid::new_v4());
        let seq = self.sequence_counter.fetch_add(1, Ordering::SeqCst);

        let tracked = TrackedJob::new(job_id.clone(), job_type, priority);
        {
            let mut jobs = self.tracked_jobs.write().await;
            jobs.insert(job_id.clone(), tracked);
        }

        let boxed_task: JobTask = Box::new(move |cancel, tx| Box::pin(task_fn(cancel, tx)));

        let item = PrioritizedItem {
            priority,
            sequence: seq,
            job_id: job_id.clone(),
            job_type,
            task: boxed_task,
        };

        {
            let mut heap = self.pending_heap.lock().await;
            heap.push(item);
        }

        let _ = self.queue_tx.send(()).await;
        job_id
    }

    /// Cooperatively cancel a job by its unique ID.
    pub async fn cancel_job(&self, job_id: &str) -> bool {
        let jobs = self.tracked_jobs.read().await;
        if let Some(job) = jobs.get(job_id) {
            job.cancel_flag.store(true, Ordering::Relaxed);
            let mut status = job.status.write().await;
            if *status == JobStatus::Pending {
                *status = JobStatus::Cancelled;
                let _ = self.event_tx.send(JobEvent::Cancelled {
                    job_id: job_id.to_string(),
                });
            }
            true
        } else {
            false
        }
    }

    /// Cooperatively cancel all pending and running jobs.
    pub async fn cancel_all(&self) {
        let jobs = self.tracked_jobs.read().await;
        for (id, job) in jobs.iter() {
            job.cancel_flag.store(true, Ordering::Relaxed);
            let mut status = job.status.write().await;
            if *status == JobStatus::Pending {
                *status = JobStatus::Cancelled;
                let _ = self.event_tx.send(JobEvent::Cancelled {
                    job_id: id.clone(),
                });
            }
        }
    }

    /// Query the current status of a tracked job.
    pub async fn get_job_status(&self, job_id: &str) -> Option<JobStatus> {
        let jobs = self.tracked_jobs.read().await;
        let job = jobs.get(job_id)?;
        let status = job.status.read().await;
        Some(*status)
    }

    /// Query the latest progress reported by a running job.
    pub async fn get_job_progress(&self, job_id: &str) -> Option<JobProgress> {
        let jobs = self.tracked_jobs.read().await;
        let job = jobs.get(job_id)?;
        let prog = job.progress.read().await;
        prog.clone()
    }

    /// Clean up old completed/failed/cancelled jobs to prevent memory leaks.
    pub async fn cleanup_old_jobs(&self, max_history: usize) {
        let mut jobs = self.tracked_jobs.write().await;
        if jobs.len() <= max_history {
            return;
        }

        let mut finished_keys = Vec::new();
        for (id, job) in jobs.iter() {
            let status = *job.status.read().await;
            if status == JobStatus::Completed || status == JobStatus::Failed || status == JobStatus::Cancelled {
                finished_keys.push((id.clone(), job.created_at));
            }
        }

        // Sort by creation time (oldest first)
        finished_keys.sort_by_key(|k| k.1);

        let to_remove = jobs.len().saturating_sub(max_history);
        for (id, _) in finished_keys.into_iter().take(to_remove) {
            jobs.remove(&id);
        }
    }

    /// Get total count of active running jobs.
    pub async fn running_jobs_count(&self) -> usize {
        let jobs = self.tracked_jobs.read().await;
        let mut count = 0;
        for job in jobs.values() {
            if *job.status.read().await == JobStatus::Running {
                count += 1;
            }
        }
        count
    }

    /// Get total count of pending jobs in queue.
    pub async fn pending_jobs_count(&self) -> usize {
        let heap = self.pending_heap.lock().await;
        heap.len()
    }
}
