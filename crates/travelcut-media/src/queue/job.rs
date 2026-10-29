//! Async job representation, cancellation tokens, and lifecycle tracking.

use crate::models::{JobId, JobPriority, JobProgress, JobStatus, JobType};
use std::sync::atomic::AtomicBool;
use std::sync::Arc;
use std::time::Instant;
use tokio::sync::RwLock;

/// Complete runtime record of a tracked job in the queue.
#[derive(Debug, Clone)]
pub struct TrackedJob {
    pub id: JobId,
    pub job_type: JobType,
    pub priority: JobPriority,
    pub created_at: Instant,
    pub started_at: Arc<RwLock<Option<Instant>>>,
    pub finished_at: Arc<RwLock<Option<Instant>>>,
    pub status: Arc<RwLock<JobStatus>>,
    pub cancel_flag: Arc<AtomicBool>,
    pub progress: Arc<RwLock<Option<JobProgress>>>,
    pub error_message: Arc<RwLock<Option<String>>>,
}

impl TrackedJob {
    /// Create a new tracked job in Pending state.
    pub fn new(id: JobId, job_type: JobType, priority: JobPriority) -> Self {
        Self {
            id,
            job_type,
            priority,
            created_at: Instant::now(),
            started_at: Arc::new(RwLock::new(None)),
            finished_at: Arc::new(RwLock::new(None)),
            status: Arc::new(RwLock::new(JobStatus::Pending)),
            cancel_flag: Arc::new(AtomicBool::new(false)),
            progress: Arc::new(RwLock::new(None)),
            error_message: Arc::new(RwLock::new(None)),
        }
    }
}
