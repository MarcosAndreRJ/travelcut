use std::sync::atomic::Ordering;
use std::time::Duration;
use tokio::time::sleep;
use travelcut_media::error::MediaEngineError;
use travelcut_media::models::{JobEvent, JobPriority, JobProgress, JobStatus, JobType};
use travelcut_media::queue::manager::JobQueueManager;

#[tokio::test]
async fn test_job_submission_and_completion() {
    let queue = JobQueueManager::new(2);
    let mut rx = queue.subscribe();

    let job_id = queue
        .submit(
            JobType::Thumbnail,
            JobPriority::High,
            |_cancel_flag, progress_tx| async move {
                let _ = progress_tx
                    .send(JobProgress {
                        job_id: "test".to_string(),
                        job_type: Some(JobType::Thumbnail),
                        percentage: 50.0,
                        message: "Halfway".to_string(),
                        ..Default::default()
                    })
                    .await;
                Ok(())
            },
        )
        .await;

    // Wait briefly for job to complete
    let mut completed = false;
    let timeout = sleep(Duration::from_millis(500));
    tokio::pin!(timeout);

    loop {
        tokio::select! {
            event = rx.recv() => {
                if let Ok(JobEvent::Completed { job_id: id, .. }) = event {
                    if id == job_id {
                        completed = true;
                        break;
                    }
                }
            }
            _ = &mut timeout => {
                break;
            }
        }
    }

    assert!(completed, "Job should have completed");
    let status = queue.get_job_status(&job_id).await;
    assert_eq!(status, Some(JobStatus::Completed));
}

#[tokio::test]
async fn test_job_cancellation() {
    let queue = JobQueueManager::new(2);

    let job_id = queue
        .submit(
            JobType::Transcode,
            JobPriority::Normal,
            |cancel_flag, _progress_tx| async move {
                for _ in 0..10 {
                    if cancel_flag.load(Ordering::Relaxed) {
                        return Err(MediaEngineError::TaskCancelled {
                            job_id: "cancel_test".to_string(),
                        });
                    }
                    sleep(Duration::from_millis(20)).await;
                }
                Ok(())
            },
        )
        .await;

    sleep(Duration::from_millis(25)).await;
    let cancelled = queue.cancel_job(&job_id).await;
    assert!(cancelled);

    // Wait briefly for cancellation to process
    sleep(Duration::from_millis(100)).await;
    let status = queue.get_job_status(&job_id).await;
    assert_eq!(status, Some(JobStatus::Cancelled));
}

#[tokio::test]
async fn test_available_permits() {
    let queue = JobQueueManager::new(4);
    assert_eq!(queue.available_permits(), 4);
}
