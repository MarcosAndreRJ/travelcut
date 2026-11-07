import { describe, it, expect, beforeEach } from 'vitest';
import {
  getReviewPlaybackRate,
  setReviewPlaybackRate,
  REVIEW_PLAYBACK_RATES
} from '../reviewPlaybackSpeed';

describe('reviewPlaybackSpeed', () => {
  beforeEach(() => {
    setReviewPlaybackRate(1);
  });

  it('inicia na velocidade normal 1x', () => {
    expect(getReviewPlaybackRate()).toBe(1);
  });

  it('suporta 0.5x, 1x, 1.5x e 2x', () => {
    expect(REVIEW_PLAYBACK_RATES).toEqual([0.5, 1, 1.5, 2]);
    setReviewPlaybackRate(0.5);
    expect(getReviewPlaybackRate()).toBe(0.5);
    setReviewPlaybackRate(1.5);
    expect(getReviewPlaybackRate()).toBe(1.5);
    setReviewPlaybackRate(2);
    expect(getReviewPlaybackRate()).toBe(2);
  });
});
