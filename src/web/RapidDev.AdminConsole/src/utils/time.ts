import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc'
import timezone from 'dayjs/plugin/timezone'
import relativeTime from 'dayjs/plugin/relativeTime'
import 'dayjs/locale/zh-cn'

dayjs.extend(utc)
dayjs.extend(timezone)
dayjs.extend(relativeTime)
dayjs.locale('zh-cn')

// ISO 字符串/Date/Unix 秒 → 按浏览器时区格式化
const ISO_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/

/** 格式化时间。默认 YYYY-MM-DD HH:mm:ss,按浏览器本地时区。 */
export function formatTime(v: any, fmt = 'YYYY-MM-DD HH:mm:ss'): string {
  if (v === null || v === undefined || v === '') return ''
  // Unix 秒(数字)
  if (typeof v === 'number' && v > 1e9 && v < 1e12) {
    const d = dayjs.unix(v)
    return d.isValid() ? d.format(fmt) : String(v)
  }
  const d = dayjs(v)
  return d.isValid() ? d.format(fmt) : String(v)
}

/** 仅日期 YYYY-MM-DD */
export function formatDate(v: any): string {
  return formatTime(v, 'YYYY-MM-DD')
}

/** 相对时间(如 "3 分钟前") */
export function fromNow(v: any): string {
  if (!v) return ''
  const d = dayjs(v)
  return d.isValid() ? d.fromNow() : String(v)
}

/** 判断值是否像 ISO 时间字符串 */
export function isTimeLike(v: any): boolean {
  return typeof v === 'string' && ISO_RE.test(v)
}
