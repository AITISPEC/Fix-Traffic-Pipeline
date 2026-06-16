import queue
import threading


class BoundedThreadPool:
	def __init__(self, max_workers=64, max_queue_size=128):
		self.max_workers = max_workers
		self._work_queue = queue.Queue(maxsize=max_queue_size)
		self._workers = []
		self._shutdown = False
		self._start_workers()

	def _start_workers(self):
		for _ in range(self.max_workers):
			t = threading.Thread(target=self._worker, daemon=True)
			t.start()
			self._workers.append(t)

	def _worker(self):
		while True:
			try:
				item = self._work_queue.get(timeout=0.1)
			except queue.Empty:
				if self._shutdown:
					break
				continue
			if item is None:
				break
			fn, args, kwargs = item
			try:
				fn(*args, **kwargs)
			except Exception:
				pass
			finally:
				self._work_queue.task_done()

	def submit(self, fn, *args, **kwargs):
		if self._shutdown:
			raise RuntimeError("Cannot submit after shutdown")
		self._work_queue.put((fn, args, kwargs), block=True)

	def shutdown(self, wait=True):
		self._shutdown = True
		if wait:
			for _ in self._workers:
				self._work_queue.put(None)
			for t in self._workers:
				t.join()
