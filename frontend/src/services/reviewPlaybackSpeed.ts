import { useState, useEffect } from 'react';

export type ReviewPlaybackRate = 0.5 | 1 | 1.5 | 2;

export const REVIEW_PLAYBACK_RATES: readonly ReviewPlaybackRate[] = [0.5, 1, 1.5, 2] as const;

let currentRate: ReviewPlaybackRate = 1;
const listeners = new Set<(rate: ReviewPlaybackRate) => void>();

export function getReviewPlaybackRate(): ReviewPlaybackRate {
  return currentRate;
}

export function setReviewPlaybackRate(rate: ReviewPlaybackRate): void {
  if (currentRate === rate) return;
  currentRate = rate;
  listeners.forEach((listener) => listener(currentRate));
}

export function useReviewPlaybackRate(): [ReviewPlaybackRate, (rate: ReviewPlaybackRate) => void] {
  const [rate, setRate] = useState<ReviewPlaybackRate>(currentRate);

  useEffect(() => {
    const listener = (newRate: ReviewPlaybackRate) => {
      setRate(newRate);
    };
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  }, []);

  return [rate, setReviewPlaybackRate];
}
