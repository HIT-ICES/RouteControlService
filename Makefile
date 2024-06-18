IMAGE_NAME = real-route-control-service
IMAGE_TAG = 0.2.2
DOCKERFILE_DIR = .
WORK_DIR = .
REPO_NAME = 192.168.1.104:5000/cloud-collaboration-platform
APP_NAME ?= $(IMAGE_NAME)
SH_APP_NAME = $(APP_NAME)_sh
DOCKER = sudo docker

K8S = kubectl -n cloud-collaboration-platform
K8S_REPLICA = 1

.PHONY: default install restart start stop logs uninstall deploy sh status

default:
	$(DOCKER) build -f $(DOCKERFILE_DIR)/Dockerfile -t $(IMAGE_NAME):$(IMAGE_TAG) $(WORK_DIR)



restart:
	$(K8S) delete pod -f -l 'app=$(APP_NAME)'

start:
	$(K8S) scale deployment -l 'app=$(APP_NAME)' --replicas=$(K8S_REPLICA)

stop:
	$(K8S) scale deployment -l 'app=$(APP_NAME)' --replicas=0

logs:
	$(K8S) logs -f -l 'app=$(APP_NAME)'

uninstall:
	IMG_TAG=$(IMAGE_TAG) SVC_NAME=$(IMAGE_NAME) IMG_REPO=$(REPO_NAME) | $(K8S) delete -f -

tag: default
	$(DOCKER) tag $(IMAGE_NAME):$(IMAGE_TAG) $(REPO_NAME)/$(IMAGE_NAME):$(IMAGE_TAG)

publish: tag
	$(DOCKER) push $(REPO_NAME)/$(IMAGE_NAME):$(IMAGE_TAG)

install:
	IMG_TAG=$(IMAGE_TAG) SVC_NAME=$(IMAGE_NAME) IMG_REPO=$(REPO_NAME) envsubst < deploy.yaml | $(K8S) apply -f -

status:
	watch $(K8S) get pods -l 'app=$(APP_NAME)'