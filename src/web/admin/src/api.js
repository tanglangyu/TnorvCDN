import axios from 'axios'

const api = axios.create({ baseURL: import.meta.env.VITE_API_BASE || '/api', timeout: 15000 })

api.interceptors.request.use((cfg) => {
  const token = localStorage.getItem('token')
  if (token) cfg.headers.Authorization = `Bearer ${token}`
  return cfg
})

api.interceptors.response.use(
  (r) => r.data,
  (err) => {
    if (err.response?.status === 401) {
      localStorage.removeItem('token')
      location.href = '/login'
    }
    return Promise.reject(err)
  }
)

export default api
