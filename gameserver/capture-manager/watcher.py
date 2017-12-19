import logging
import time
from threading import Thread

import config


logger = logging.getLogger(__name__)


class Watcher(Thread):
	"""
	Watch all gameservers, publish their data if no data received from
	game for a while (i.e. due to end of op, or server crash).
	"""
	def __init__(self, gameservers):
		Thread.__init__(self)
		self.daemon = True

		self.gameservers = gameservers

	def run(self):
		logger.debug('Running')

		while True:
			time.sleep(config.CAPTURE_TIMEOUT)
			logger.debug('Checking gameservers...')
			time_now = time.time()

			for server_id, server in self.gameservers.items():
				logger.debug('Checking: {}'.format(server_id))

				if not server.is_capturing:
					logger.debug('  Skipping (not capturing)')
					continue

				time_delta = time_now - server.last_import_time
				logger.debug('  Seconds since last import: {}'.format(round(time_delta, 1)))
				if time_delta > config.CAPTURE_TIMEOUT and server.is_capturing:
					logger.debug('  Timed out. Publishing...'.format(server_id))
					server.publish()
					self.gameservers.pop(server_id, None)