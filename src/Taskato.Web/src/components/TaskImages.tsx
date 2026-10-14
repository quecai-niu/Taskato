import { useEffect, useRef, useState, type ReactNode, type ClipboardEvent } from "react";
import { ImageSquare } from "@phosphor-icons/react";
import { bridge, type TaskImageDto } from "../bridge";

const IMAGE_TYPES = new Set(["image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp"]);
const MAX_IMAGE_BYTES = 10 * 1024 * 1024;

export async function prepareImage(file: File) {
  if (!IMAGE_TYPES.has(file.type))
    throw new Error("支持 PNG、JPEG、GIF、BMP 和 WebP 图片。");
  if (file.size === 0 || file.size > MAX_IMAGE_BYTES)
    throw new Error(`图片“${file.name}”为空或超过 10 MB。`);

  const dataUrl = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(new Error(`无法读取图片“${file.name}”。`));
    reader.readAsDataURL(file);
  });
  const image = new Image();
  image.src = dataUrl;
  try { await image.decode(); }
  catch { throw new Error(`无法解码图片“${file.name}”，请检查文件是否损坏。`); }
  if (image.naturalWidth * image.naturalHeight > 40_000_000)
    throw new Error("图片分辨率超过 4000 万像素，请缩小后再添加。");

  const scale = Math.min(1, 240 / Math.max(image.naturalWidth, image.naturalHeight));
  const canvas = document.createElement("canvas");
  canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
  canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
  canvas.getContext("2d")!.drawImage(image, 0, 0, canvas.width, canvas.height);
  return {
    fileName: file.name || `截图-${Date.now()}.png`,
    contentType: file.type,
    data: dataUrl.slice(dataUrl.indexOf(",") + 1),
    thumbnail: canvas.toDataURL("image/png").split(",")[1],
  };
}

export function TaskImages({ taskId, children }: { taskId: number; children: ReactNode }) {
  const [images, setImages] = useState<TaskImageDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [previewId, setPreviewId] = useState<number | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const busyRef = useRef(false);
  const mounted = useRef(false);
  const loadRevision = useRef(0);

  async function refresh() {
    const revision = ++loadRevision.current;
    try {
      const result = await bridge.request<TaskImageDto[]>("tasks.images.list", { taskId });
      if (mounted.current && revision === loadRevision.current) setImages(result);
    } catch (reason) {
      if (mounted.current && revision === loadRevision.current) setError((reason as Error).message);
    } finally {
      if (mounted.current && revision === loadRevision.current) setLoading(false);
    }
  }

  useEffect(() => {
    mounted.current = true;
    void refresh();
    const unsubscribe = bridge.subscribe("tasks.images.changed", (payload) => {
      if ((payload as { taskId: number }).taskId === taskId) void refresh();
    });
    return () => { mounted.current = false; unsubscribe(); };
  }, [taskId]);

  async function addImages(files: File[]) {
    if (busyRef.current) {
      setError("正在处理图片，请完成后再添加。");
      return;
    }
    busyRef.current = true;
    setBusy(true);
    setError("");
    try {
      for (const file of files) {
        const image = await prepareImage(file);
        await bridge.request("tasks.images.add", { taskId, ...image });
        await refresh();
      }
    } catch (reason) {
      if (mounted.current) setError((reason as Error).message);
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  function pasteImages(event: ClipboardEvent<HTMLDivElement>) {
    const files = clipboardImages(event);
    if (files.length === 0) return;
    event.preventDefault();
    void addImages(files);
  }

  async function deleteImage(image: TaskImageDto) {
    busyRef.current = true;
    setBusy(true);
    setError("");
    try {
      await bridge.request("tasks.images.delete", { taskId, imageId: image.id });
      await refresh();
    } catch (reason) {
      if (mounted.current) setError((reason as Error).message);
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  return (
    <div onPaste={pasteImages}>
      {children}
      <section className="content-section task-images-section" aria-label="图片附件" tabIndex={0}>
        <div className="section-title-row">
          <h3>图片附件</h3>
          <button type="button" className="secondary-button" disabled={busy} onClick={() => fileInput.current!.click()}>
            {busy ? "正在处理…" : "添加图片"}
          </button>
          <input ref={fileInput} type="file" hidden multiple accept="image/png,image/jpeg,image/gif,image/bmp,image/webp"
            onChange={(event) => {
              const files = Array.from(event.target.files!);
              event.target.value = "";
              if (files.length > 0) void addImages(files);
            }} />
        </div>
        <p className="task-images-help">选择图片，或在任务内容和此区域按 Ctrl+V 粘贴截图。每张最多 10 MB。</p>
        {error && <div className="task-images-error" role="alert">{error} <button type="button" className="secondary-button" disabled={busy} onClick={() => { setError(""); void refresh(); }}>重新加载</button></div>}
        {loading && <p role="status">正在加载图片…</p>}
        {!loading && !images.length && !error && <p className="task-images-help">暂无图片附件</p>}
        <div className="task-images-grid">
          {images.map((image) => (
            <article className="task-image-card" key={image.id}>
              <button type="button" className="task-image-thumbnail" disabled={busy} aria-label={`查看大图：${image.fileName}`} onClick={() => setPreviewId(image.id)}>
                <img src={image.thumbnailUrl} alt={image.fileName} />
              </button>
              <span title={image.fileName}>{image.fileName}</span>
              <button type="button" className="secondary-button" disabled={busy} aria-label={`删除图片：${image.fileName}`} onClick={() => void deleteImage(image)}>删除</button>
            </article>
          ))}
        </div>
      </section>
      {previewId !== null && <TaskImageViewer taskId={taskId} initialImageId={previewId} onClose={() => setPreviewId(null)} />}

    </div>
  );
}

export type PreparedTaskImage = Awaited<ReturnType<typeof prepareImage>>;

export function clipboardImages(event: ClipboardEvent): File[] {
  const files: File[] = [];
  for (const item of Array.from(event.clipboardData.items)) {
    if (item.kind === "file" && item.type.startsWith("image/")) {
      const file = item.getAsFile();
      if (file) {
        const extension = file.type.split("/")[1].replace("jpeg", "jpg");
        files.push(new File([file], `截图-${Date.now()}.${extension}`, { type: file.type }));
      }
    }
  }
  return files;
}

export function DraftImages({ images, disabled, onRemove }: {
  images: PreparedTaskImage[]; disabled: boolean; onRemove(index: number): void;
}) {
  return <div className="task-images-grid">
    {images.map((image, index) => <article className="task-image-card" key={index}>
      <img className="task-draft-thumbnail" src={`data:image/png;base64,${image.thumbnail}`} alt={image.fileName} />
      <button type="button" className="secondary-button" disabled={disabled} onClick={() => onRemove(index)} aria-label={`移除待保存图片：${image.fileName}`}>移除</button>
    </article>)}
  </div>;
}

export function TaskImageButton({ taskId }: { taskId: number }) {
  const [open, setOpen] = useState(false);
  return <>
    <button type="button" className="task-image-indicator" data-task-reorder-ignore
      title="查看任务附图" aria-label="查看任务附图" onClick={(event) => { event.stopPropagation(); setOpen(true); }}>
      <ImageSquare size={17} />
    </button>
    {open && <TaskImageViewer taskId={taskId} onClose={() => setOpen(false)} />}
  </>;
}

function TaskImageViewer({ taskId, initialImageId, onClose }: {
  taskId: number; initialImageId?: number; onClose(): void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [images, setImages] = useState<TaskImageDto[]>([]);
  const [index, setIndex] = useState(0);
  const [url, setUrl] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    dialog.current!.showModal();
    let active = true;
    bridge.request<TaskImageDto[]>("tasks.images.list", { taskId }).then((result) => {
      if (!active) return;
      setImages(result);
      setIndex(Math.max(0, result.findIndex((image) => image.id === initialImageId)));
      setLoading(false);
    }).catch((reason: Error) => {
      if (active) {
        setError(reason.message);
        setLoading(false);
      }
    });
    return () => { active = false; };
  }, [taskId, initialImageId]);
  useEffect(() => {
    if (!images.length) return;
    let active = true;
    setUrl("");
    setError("");
    bridge.request<{ url: string }>("tasks.images.get", { taskId, imageId: images[index].id })
      .then((result) => { if (active) setUrl(result.url); })
      .catch((reason: Error) => { if (active) setError(reason.message); });
    return () => { active = false; };
  }, [taskId, images, index]);
  const image = images[index];
  return <dialog ref={dialog} className="task-image-dialog" aria-label="任务图片预览"
    onPointerDown={(event) => event.stopPropagation()}
    onContextMenu={(event) => event.stopPropagation()}
    onClose={onClose} onClick={(event) => { event.stopPropagation(); if (event.target === event.currentTarget) onClose(); }}
    onKeyDown={(event) => {
      event.stopPropagation();
      if (event.key === "ArrowLeft" && index > 0) { event.preventDefault(); setIndex(index - 1); }
      if (event.key === "ArrowRight" && index < images.length - 1) { event.preventDefault(); setIndex(index + 1); }
    }}>
    <div className="section-title-row"><span>{image?.fileName ?? "任务附图"}</span>
      <button type="button" className="secondary-button" autoFocus onClick={onClose}>关闭</button></div>
    {error && <p role="alert" className="task-images-error">{error}</p>}
    {loading && <p role="status">正在加载图片…</p>}
    {!loading && !error && images.length === 0 && <p>暂无图片附件</p>}
    {image && <>
      <div className="task-image-navigation">
        <button type="button" className="secondary-button" disabled={index === 0} onClick={() => setIndex(index - 1)} aria-label="上一张图片">← 上一张</button>
        <span aria-live="polite">{index + 1} / {images.length}</span>
        <button type="button" className="secondary-button" disabled={index === images.length - 1} onClick={() => setIndex(index + 1)} aria-label="下一张图片">下一张 →</button>
      </div>
      {!url && !error && <p role="status">正在加载原图…</p>}
      {url && <img src={url} alt={image.fileName} />}
    </>}
  </dialog>;
}
