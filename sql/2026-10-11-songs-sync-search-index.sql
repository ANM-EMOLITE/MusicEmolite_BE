-- Đồng bộ nhạc từ Youtube + tìm kiếm nhanh bằng index.
-- PHẢI chạy script này TRƯỚC khi deploy code BE mới (entity DistSongs đã map cột search_text).
--
-- Extension cài thẳng vào schema dist và mọi chỗ gọi đều ghi rõ schema,
-- vì DB không có schema public / search_path không trỏ tới schema nào
-- (lỗi "no schema has been selected to create in").
-- Nếu extension đã từng được cài ở schema khác, kiểm tra bằng:
--   SELECT e.extname, n.nspname FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace;

CREATE EXTENSION IF NOT EXISTS pg_trgm SCHEMA dist;
CREATE EXTENSION IF NOT EXISTS unaccent SCHEMA dist;

-- 1. Cột tìm kiếm: "tên bài + tên ca sĩ", chữ thường, không dấu (vd: "lac troi son tung m-tp")
ALTER TABLE dist.songs ADD COLUMN IF NOT EXISTS search_text TEXT NULL;

-- unaccent 1 tham số tự tìm dictionary theo search_path -> truyền dictionary có schema để chắc chắn
CREATE OR REPLACE FUNCTION dist.fn_songs_search_text()
RETURNS TRIGGER AS $$
BEGIN
  NEW.search_text := lower(dist.unaccent('dist.unaccent'::regdictionary,
    coalesce(NEW.title, '') || ' ' ||
    coalesce((SELECT a.name FROM dist.artists a WHERE a.id = NEW.artist_id), '')
  ));
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_songs_search_text ON dist.songs;
CREATE TRIGGER trg_songs_search_text
  BEFORE INSERT OR UPDATE OF title, artist_id ON dist.songs
  FOR EACH ROW EXECUTE FUNCTION dist.fn_songs_search_text();

-- Backfill dữ liệu cũ
UPDATE dist.songs s
SET search_text = lower(dist.unaccent('dist.unaccent'::regdictionary,
  coalesce(s.title, '') || ' ' ||
  coalesce((SELECT a.name FROM dist.artists a WHERE a.id = s.artist_id), '')
));

-- Trigram GIN index: LIKE '%từ khóa%' dùng được index
CREATE INDEX IF NOT EXISTS ix_songs_search_text_trgm
  ON dist.songs USING gin (search_text dist.gin_trgm_ops);

-- 2. Chống trùng bài Youtube.
-- Nếu lệnh dưới lỗi do dữ liệu cũ đã trùng, kiểm tra bằng:
--   SELECT youtube_video_id, count(*) FROM dist.songs
--   WHERE youtube_video_id IS NOT NULL AND is_deleted = false
--   GROUP BY youtube_video_id HAVING count(*) > 1;
CREATE UNIQUE INDEX IF NOT EXISTS ux_songs_youtube_video_id
  ON dist.songs (youtube_video_id)
  WHERE youtube_video_id IS NOT NULL AND is_deleted = false;

-- 3. Phục vụ đồng bộ: tìm artist theo tên, lyrics theo bài
CREATE INDEX IF NOT EXISTS ix_artists_lower_name ON dist.artists (lower(name));
CREATE INDEX IF NOT EXISTS ix_song_lyrics_song_id ON dist.song_lyrics (song_id);
