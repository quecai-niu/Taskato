import { useEffect, useRef, useState, type ReactNode, type ClipboardEvent } from "react";
import { bridge, type TaskImageDto } from "../bridge";

const IMAGE_TYPES = new Set(["image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp"]);
const MAX_IMAGE_BYTES = 10 * 1024 * 1024;

async function prepareImage(file: File) {
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
  const [preview, setPreview] = useState<{ url: string; name: string } | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const dialog = useRef<HTMLDialogElement>(null);
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

  useEffect(() => {
    if (preview) dialog.current!.showModal();
    else dialog.current!.close();
  }, [preview]);

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
    if (files.length === 0) return;
    event.preventDefault();
    void addImages(files);
  }

  async function openImage(image: TaskImageDto) {
    busyRef.current = true;
    setBusy(true);
    setError("");
    try {
      const result = await bridge.request<{ url: string }>("tasks.images.get", { taskId, imageId: image.id });
      if (mounted.current) setPreview({ url: result.url, name: image.fileName });
    } catch (reason) {
      if (mounted.current) setError((reason as Error).message);
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
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
              <button type="button" className="task-image-thumbnail" disabled={busy} aria-label={`查看大图：${image.fileName}`} onClick={() => void openImage(image)}>
                <img src={image.thumbnailUrl} alt={image.fileName} />
              </button>
              <span title={image.fileName}>{image.fileName}</span>
              <button type="button" className="secondary-button" disabled={busy} aria-label={`删除图片：${image.fileName}`} onClick={() => void deleteImage(image)}>删除</button>
            </article>
          ))}
        </div>
      </section>
      <dialog ref={dialog} className="task-image-dialog" aria-label="图片预览" onClose={() => setPreview(null)}
        onClick={(event) => { if (event.target === event.currentTarget) setPreview(null); }}>
        {preview && <><div className="section-title-row"><span>{preview.name}</span><button type="button" className="secondary-button" autoFocus onClick={() => setPreview(null)}>关闭</button></div><img src={preview.url} alt={preview.name} /></>}
      </dialog>
    </div>
  );
}
