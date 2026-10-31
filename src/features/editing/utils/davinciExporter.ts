import {
  UnifiedMediaAsset,
  UnifiedTimelineClip,
  UnifiedCutSuggestion,
  DaVinciExportData
} from '../types/editingTypes';

/**
 * Formats seconds into SMPTE timecode (HH:MM:SS:FF)
 */
export function secondsToTimecode(secs: number, fps: number = 60): string {
  const totalSeconds = Math.max(0, secs);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = Math.floor(totalSeconds % 60);
  const frames = Math.min(fps - 1, Math.round((totalSeconds % 1) * fps));

  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}:${pad(frames)}`;
}

/**
 * Converts seconds to frame counts based on target framerate
 */
export function secondsToFrames(secs: number, fps: number = 60): number {
  return Math.round(Math.max(0, secs) * fps);
}

/**
 * Generates DaVinci Resolve / FCP7 XML exchange format
 */
export function generateDaVinciXML(
  projectName: string,
  timelineClips: UnifiedTimelineClip[],
  mediaAssets: UnifiedMediaAsset[],
  fps: number = 60
): string {
  const sequenceDurationFrames = timelineClips.reduce(
    (max, clip) => Math.max(max, secondsToFrames(clip.timelineStartSec + clip.durationSec, fps)),
    0
  );

  const clipEntriesXml = timelineClips
    .map((clip, index) => {
      const asset = mediaAssets.find((a) => a.id === clip.mediaClipId);
      const fileName = asset?.fileName || `${clip.title}.mp4`;
      const filePath = asset?.sourcePath || `D:\\Videos\\${fileName}`;
      const startFrame = secondsToFrames(clip.timelineStartSec, fps);
      const endFrame = secondsToFrames(clip.timelineStartSec + clip.durationSec, fps);
      const inFrame = secondsToFrames(clip.sourceInSec, fps);
      const outFrame = secondsToFrames(clip.sourceOutSec, fps);
      const durationFrames = secondsToFrames(clip.durationSec, fps);

      return `        <clipitem id="clipitem-${clip.id}">
          <name>${clip.title}</name>
          <duration>${durationFrames}</duration>
          <rate>
            <timebase>${fps}</timebase>
            <ntsc>FALSE</ntsc>
          </rate>
          <start>${startFrame}</start>
          <end>${endFrame}</end>
          <in>${inFrame}</in>
          <out>${outFrame}</out>
          <file id="file-${clip.mediaClipId}">
            <name>${fileName}</name>
            <pathurl>file://localhost/${filePath.replace(/\\/g, '/')}</pathurl>
            <rate>
              <timebase>${asset?.frameRate || fps}</timebase>
              <ntsc>FALSE</ntsc>
            </rate>
            <duration>${secondsToFrames(asset?.durationSec || 300, fps)}</duration>
            <media>
              <video>
                <samplecharacteristics>
                  <width>3840</width>
                  <height>2160</height>
                </samplecharacteristics>
              </video>
            </media>
          </file>
          <filter>
            <effect>
              <name>LUT / Color Grading</name>
              <effectid>${clip.colorGradeLut}</effectid>
            </effect>
          </filter>
        </clipitem>`;
    })
    .join('\n');

  return `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE xmeml>
<xmeml version="5">
  <project>
    <name>${projectName}</name>
    <children>
      <sequence id="sequence-1">
        <name>${projectName} - Linha do Tempo Montada IA</name>
        <duration>${sequenceDurationFrames}</duration>
        <rate>
          <timebase>${fps}</timebase>
          <ntsc>FALSE</ntsc>
        </rate>
        <timecode>
          <rate>
            <timebase>${fps}</timebase>
            <ntsc>FALSE</ntsc>
          </rate>
          <string>01:00:00:00</string>
          <frame>216000</frame>
          <displayformat>NDF</displayformat>
        </timecode>
        <media>
          <video>
            <format>
              <samplecharacteristics>
                <width>3840</width>
                <height>2160</height>
                <pixelaspectratio>square</pixelaspectratio>
                <rate>
                  <timebase>${fps}</timebase>
                  <ntsc>FALSE</ntsc>
                </rate>
                <codec>
                  <name>Apple ProRes 422 HQ</name>
                </codec>
              </samplecharacteristics>
            </format>
            <track>
${clipEntriesXml}
            </track>
          </video>
          <audio>
            <track>
              <enabled>TRUE</enabled>
              <locked>FALSE</locked>
            </track>
          </audio>
        </media>
      </sequence>
    </children>
  </project>
</xmeml>`;
}

/**
 * Generates CMX 3600 Edit Decision List (EDL)
 */
export function generateDaVinciEDL(
  projectName: string,
  timelineClips: UnifiedTimelineClip[],
  mediaAssets: UnifiedMediaAsset[],
  fps: number = 60
): string {
  const header = `TITLE: ${projectName.toUpperCase()}\nFCM: NON-DROP FRAME\n\n`;

  const lines = timelineClips.map((clip, idx) => {
    const editNum = (idx + 1).toString().padStart(3, '0');
    const asset = mediaAssets.find((a) => a.id === clip.mediaClipId);
    const reel = (asset?.fileName || 'AX').replace(/[^a-zA-Z0-9]/g, '').slice(0, 8).toUpperCase().padEnd(8, ' ');
    const track = clip.trackIndex === 0 ? 'V     C' : 'A     C';

    const srcIn = secondsToTimecode(clip.sourceInSec, fps);
    const srcOut = secondsToTimecode(clip.sourceOutSec, fps);
    const dstIn = secondsToTimecode(clip.timelineStartSec, fps);
    const dstOut = secondsToTimecode(clip.timelineStartSec + clip.durationSec, fps);

    return `${editNum}  ${reel} ${track}        ${srcIn} ${srcOut} ${dstIn} ${dstOut}\n* FROM CLIP: ${clip.title} (LUT: ${clip.colorGradeLut})`;
  });

  return header + lines.join('\n\n');
}

/**
 * Computes complete export analytics and exchange file contents
 */
export function computeExportData(
  projectName: string,
  timelineClips: UnifiedTimelineClip[],
  mediaAssets: UnifiedMediaAsset[],
  cuts: UnifiedCutSuggestion[],
  fps: number = 60
): DaVinciExportData {
  const totalOriginalSec = mediaAssets.reduce((sum, a) => sum + a.durationSec, 0);
  const totalEditedSec = timelineClips.reduce(
    (max, c) => Math.max(max, c.timelineStartSec + c.durationSec),
    0
  );

  const cutsAppliedCount = cuts.filter((c) => c.decisionState === 'accepted').length;
  const cutsRejectedCount = cuts.filter((c) => c.decisionState === 'rejected').length;

  const totalCutsSavedSec = cuts
    .filter((c) => c.decisionState === 'accepted')
    .reduce((sum, c) => sum + c.durationSec, 0);

  const savedSeconds = Math.max(0, totalCutsSavedSec);
  const percentageSaved = totalOriginalSec > 0 ? Math.round((savedSeconds / totalOriginalSec) * 100) : 0;

  const xmlContent = generateDaVinciXML(projectName, timelineClips, mediaAssets, fps);
  const edlContent = generateDaVinciEDL(projectName, timelineClips, mediaAssets, fps);

  return {
    projectName,
    fps,
    totalOriginalSec,
    totalEditedSec,
    savedSeconds,
    percentageSaved,
    cutsAppliedCount,
    cutsRejectedCount,
    clipsCount: timelineClips.length,
    xmlContent,
    edlContent,
    timelineClips
  };
}

/**
 * Triggers a download in the browser for exported text content
 */
export function downloadExportFile(filename: string, content: string, mimeType: string = 'text/xml'): void {
  const blob = new Blob([content], { type: `${mimeType};charset=utf-8;` });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', filename);
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
