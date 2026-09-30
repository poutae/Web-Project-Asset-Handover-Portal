import { Route, Routes } from 'react-router-dom'

export function App() {
  return (
    <Routes>
      <Route path="/" element={<h1 className="p-6 text-2xl font-semibold">Asset &amp; Handover Portal</h1>} />
    </Routes>
  )
}
