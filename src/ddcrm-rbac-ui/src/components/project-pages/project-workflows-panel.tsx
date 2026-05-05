"use client";
/* eslint-disable react-hooks/set-state-in-effect */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  addEdge,
  applyNodeChanges,
  applyEdgeChanges,
  Background,
  BackgroundVariant,
  Controls,
  type DefaultEdgeOptions,
  Handle,
  MiniMap,
  Position,
  ReactFlow,
  type Connection,
  type Edge,
  type EdgeChange,
  type NodeChange,
  type Node,
  type NodeProps,
  type ReactFlowInstance,
  useEdgesState,
} from "@xyflow/react";
import { memo, useCallback, useEffect, useMemo, useRef, useState } from "react";
import type {
  ApiSession,
  Offer,
  WorkflowDraft,
  WorkflowDraftUi,
  WorkflowExecution,
  WorkflowNode,
} from "@/lib/api-client";
import {
  getOfferWorkflowDraftRequest,
  listOfferWorkflowExecutionsRequest,
  listProjectIntegrationsStatusRequest,
  listOffersRequest,
  publishOfferWorkflowRequest,
  saveOfferWorkflowDraftRequest,
} from "@/lib/api-client";
import { hasPermission, projectPermissions, type ProjectRole } from "@/lib/rbac";

interface ProjectWorkflowsPanelProps {
  apiSession: ApiSession;
  projectId: string;
  currentRole: ProjectRole;
}

interface WorkflowEditorNodeData {
  [key: string]: unknown;
  nodeType: WorkflowNode["type"];
  name: string;
  config: Record<string, unknown>;
  isEntry: boolean;
}

interface WorkflowEditorEdgeData {
  [key: string]: unknown;
  condition: string;
}

interface WorkflowTypedEditorState {
  conditionField: string;
  conditionEquals: string;
  setVariablesText: string;
  selectPlatform: string;
  workerAction: string;
  customIntegrationId: string;
  customMethod: string;
  customPath: string;
  customHeadersText: string;
  customPayloadText: string;
  steamAction: string;
  steamAccountId: string;
  steamDelaySeconds: string;
  steamPayloadText: string;
  taskType: string;
  taskTitle: string;
  taskAssignee: string;
  taskDelaySeconds: string;
  taskPayloadText: string;
  buyerMessage: string;
  notifyMessage: string;
}

interface WorkflowNodeCatalogItem {
  type: WorkflowNode["type"];
  label: string;
  description: string;
  tone: "start" | "branch" | "data" | "input" | "routing" | "worker" | "http" | "integration" | "task" | "response" | "notify" | "end";
  group: "start" | "base" | "integration";
  integrationKey?: string;
}

interface WorkflowNodePort {
  id: string;
  label: string;
  description: string;
  valueType?: string;
}

interface WorkflowNodePortSet {
  inputs: readonly WorkflowNodePort[];
  outputs: readonly WorkflowNodePort[];
}

interface WorkflowFieldDescriptor {
  key: string;
  label: string;
  description: string;
  required: boolean;
  type: "string" | "guid" | "json-object" | "int";
  placeholder?: string;
}

const workflowNodeGuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const workflowHttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"] as const;

const workflowNodeCatalog: readonly WorkflowNodeCatalogItem[] = [
  { type: "PurchaseStart", label: "Purchase", description: "Входящее событие покупки.", tone: "start", group: "start" },
  { type: "MessageStart", label: "Message", description: "Входящее событие сообщения покупателя.", tone: "start", group: "start" },
  { type: "ReviewStart", label: "Review", description: "Входящее событие отзыва/оценки.", tone: "start", group: "start" },
  { type: "Condition", label: "Condition", description: "Ветвление по переменным.", tone: "branch", group: "base" },
  { type: "SetVariables", label: "Set Variables", description: "Устанавливает/переопределяет runtime-переменные.", tone: "data", group: "base" },
  { type: "LoadOffer", label: "Load Offer", description: "Загружает Offer и variants.", tone: "input", group: "base" },
  { type: "SelectAccountPriorityFallback", label: "Select Account", description: "Приоритет + fallback.", tone: "routing", group: "base" },
  { type: "InvokeWorkerAction", label: "Invoke Worker", description: "Вызывает действие воркера.", tone: "worker", group: "base" },
  { type: "InvokeCustomHttp", label: "Invoke HTTP", description: "Вызывает custom HTTP integration.", tone: "http", group: "integration", integrationKey: "custom-http" },
  { type: "SteamAction", label: "Steam Action", description: "Интеграционная node Steam (jobs/actions).", tone: "integration", group: "integration", integrationKey: "steam-accounts-manager" },
  { type: "Task", label: "Task", description: "Планирует отложенную задачу (например через 3 часа).", tone: "task", group: "base" },
  { type: "SendBuyerResponse", label: "Buyer Response", description: "Формирует ответ покупателю.", tone: "response", group: "base" },
  { type: "Notify", label: "Notify", description: "Служебное уведомление.", tone: "notify", group: "base" },
  { type: "End", label: "End", description: "Завершает workflow.", tone: "end", group: "base" },
] as const;

const workflowNodeTypes: readonly WorkflowNode["type"][] = workflowNodeCatalog.map((item) => item.type);
const workflowStartNodeTypes: readonly WorkflowNode["type"][] = ["PurchaseStart", "MessageStart", "ReviewStart"] as const;

const nodeToneByType = workflowNodeCatalog.reduce((acc, item) => {
  acc[item.type] = item.tone;
  return acc;
}, {} as Record<WorkflowNode["type"], WorkflowNodeCatalogItem["tone"]>);

const nodeLabelByType = workflowNodeCatalog.reduce((acc, item) => {
  acc[item.type] = item.label;
  return acc;
}, {} as Record<WorkflowNode["type"], string>);

const workflowNodeCatalogByType = workflowNodeCatalog.reduce((acc, item) => {
  acc[item.type] = item;
  return acc;
}, {} as Record<WorkflowNode["type"], WorkflowNodeCatalogItem>);

const nodePortsByType: Record<WorkflowNode["type"], WorkflowNodePortSet> = {
  PurchaseStart: {
    inputs: [],
    outputs: [
      { id: "out-flow", label: "flow", description: "Основной поток исполнения.", valueType: "flow" },
      { id: "out-platform", label: "event.platform", description: "Площадка продажи (steam/funpay/etc).", valueType: "string?" },
      { id: "out-quantity", label: "event.quantity", description: "Количество товара из события, если передано.", valueType: "number?" },
      { id: "out-amount", label: "event.amount", description: "Сумма покупки, если передана.", valueType: "number?" },
      { id: "out-currency", label: "event.currency", description: "Валюта покупки (RUB/USD/...).", valueType: "string?" },
      { id: "out-source-order-id", label: "purchase.sourceOrderId", description: "Внешний ID заказа.", valueType: "string" },
      { id: "out-buyer-id", label: "purchase.buyerId", description: "ID покупателя из webhook.", valueType: "string?" },
      { id: "out-payload", label: "purchase.payload", description: "Сырой payload события покупки.", valueType: "object?" },
      { id: "out-offer-id", label: "purchase.offerId", description: "ID offer, по которому пришла покупка.", valueType: "uuid" },
    ],
  },
  MessageStart: {
    inputs: [],
    outputs: [
      { id: "out-flow", label: "flow", description: "Основной поток исполнения.", valueType: "flow" },
      { id: "out-platform", label: "event.platform", description: "Площадка, откуда пришло сообщение.", valueType: "string?" },
      { id: "out-quantity", label: "event.quantity", description: "Количество, если пришло в payload события.", valueType: "number?" },
      { id: "out-amount", label: "event.amount", description: "Сумма, если пришла в payload события.", valueType: "number?" },
      { id: "out-currency", label: "event.currency", description: "Валюта из payload события.", valueType: "string?" },
      { id: "out-message-text", label: "message.text", description: "Текст сообщения от пользователя.", valueType: "string?" },
      { id: "out-buyer-id", label: "message.buyerId", description: "ID пользователя/покупателя.", valueType: "string?" },
      { id: "out-source-order-id", label: "message.sourceOrderId", description: "Связанный ID заказа (если есть).", valueType: "string" },
      { id: "out-payload", label: "message.payload", description: "Сырой payload сообщения.", valueType: "object?" },
    ],
  },
  ReviewStart: {
    inputs: [],
    outputs: [
      { id: "out-flow", label: "flow", description: "Основной поток исполнения.", valueType: "flow" },
      { id: "out-platform", label: "event.platform", description: "Площадка, где оставлен отзыв.", valueType: "string?" },
      { id: "out-quantity", label: "event.quantity", description: "Количество, если передано вместе с отзывом.", valueType: "number?" },
      { id: "out-amount", label: "event.amount", description: "Сумма заказа, если передана.", valueType: "number?" },
      { id: "out-currency", label: "event.currency", description: "Валюта заказа, если передана.", valueType: "string?" },
      { id: "out-review-text", label: "review.text", description: "Текст отзыва.", valueType: "string?" },
      { id: "out-review-rating", label: "review.rating", description: "Оценка отзыва (например 1..5).", valueType: "number?" },
      { id: "out-source-order-id", label: "review.sourceOrderId", description: "ID заказа, к которому относится отзыв.", valueType: "string" },
      { id: "out-payload", label: "review.payload", description: "Сырой payload отзыва.", valueType: "object?" },
    ],
  },
  Condition: {
    inputs: [
      { id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" },
      { id: "in-field", label: "field", description: "Имя runtime-поля для проверки.", valueType: "string" },
      { id: "in-equals", label: "equals", description: "Ожидаемое значение.", valueType: "string" },
    ],
    outputs: [
      { id: "out-true", label: "true", description: "Ветка при успешном сравнении.", valueType: "flow" },
      { id: "out-false", label: "false", description: "Ветка при неуспешном сравнении.", valueType: "flow" },
      { id: "out-next", label: "next", description: "Fallback-поток без условия.", valueType: "flow" },
    ],
  },
  SetVariables: {
    inputs: [
      { id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" },
      { id: "in-values", label: "values", description: "JSON объект переменных для установки.", valueType: "object" },
    ],
    outputs: [
      { id: "out-flow", label: "next", description: "Выход потока после установки переменных.", valueType: "flow" },
      { id: "out-values", label: "variables.*", description: "Обновлённые runtime-переменные.", valueType: "object" },
    ],
  },
  LoadOffer: {
    inputs: [{ id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" }],
    outputs: [
      { id: "out-offer", label: "offer.*", description: "Данные offer (id/name/status).", valueType: "object" },
      { id: "out-variants", label: "offerVariants", description: "Список активных variants offer.", valueType: "array" },
      { id: "out-next", label: "next", description: "Выход потока.", valueType: "flow" },
    ],
  },
  SelectAccountPriorityFallback: {
    inputs: [
      { id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" },
      { id: "in-platform", label: "platform", description: "Платформа для приоритизации.", valueType: "string?" },
      { id: "in-variants", label: "offerVariants", description: "Входной список вариантов offer.", valueType: "array" },
    ],
    outputs: [
      { id: "out-selected", label: "selectedVariant.*", description: "Выбранный вариант и аккаунт.", valueType: "object" },
      { id: "out-next", label: "next", description: "Выход потока.", valueType: "flow" },
    ],
  },
  InvokeWorkerAction: {
    inputs: [
      { id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" },
      { id: "in-action", label: "action", description: "Worker action key.", valueType: "string" },
    ],
    outputs: [
      { id: "out-worker", label: "workerAction.*", description: "Результат постановки worker action.", valueType: "object" },
      { id: "out-next", label: "next", description: "Выход потока.", valueType: "flow" },
    ],
  },
  InvokeCustomHttp: {
    inputs: [
      { id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" },
      { id: "in-integration", label: "integrationId", description: "GUID custom HTTP integration.", valueType: "guid" },
      { id: "in-method", label: "method", description: "HTTP метод запроса.", valueType: "string" },
      { id: "in-path", label: "path", description: "Относительный path к baseUrl.", valueType: "string?" },
      { id: "in-headers", label: "headers", description: "JSON object заголовков.", valueType: "object?" },
      { id: "in-payload", label: "payload", description: "JSON object тела запроса.", valueType: "object?" },
    ],
    outputs: [
      { id: "out-http", label: "customHttp.*", description: "Результат HTTP вызова (status/body).", valueType: "object" },
      { id: "out-next", label: "next", description: "Выход потока.", valueType: "flow" },
    ],
  },
  SteamAction: {
    inputs: [
      { id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" },
      { id: "in-action", label: "action", description: "Steam action/job type.", valueType: "string" },
      { id: "in-account-id", label: "accountId", description: "ID Steam аккаунта (опционально).", valueType: "guid?" },
      { id: "in-delay-seconds", label: "delaySeconds", description: "Отложенный запуск в секундах.", valueType: "number?" },
      { id: "in-payload", label: "payload", description: "Дополнительные параметры задачи.", valueType: "object?" },
    ],
    outputs: [
      { id: "out-steam", label: "steam.action.*", description: "Результат постановки steam action.", valueType: "object" },
      { id: "out-next", label: "next", description: "Выход потока.", valueType: "flow" },
    ],
  },
  Task: {
    inputs: [
      { id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" },
      { id: "in-task-type", label: "taskType", description: "Ключ типа задачи.", valueType: "string" },
      { id: "in-delay", label: "delaySeconds", description: "Задержка перед задачей (сек).", valueType: "number?" },
      { id: "in-payload", label: "payload", description: "Произвольный payload задачи.", valueType: "object?" },
    ],
    outputs: [
      { id: "out-task", label: "task.*", description: "Созданная/запланированная задача.", valueType: "object" },
      { id: "out-next", label: "next", description: "Выход потока.", valueType: "flow" },
    ],
  },
  SendBuyerResponse: {
    inputs: [
      { id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" },
      { id: "in-message", label: "message", description: "Текст/шаблон ответа.", valueType: "string" },
      { id: "in-selected", label: "selectedVariant", description: "Выбранный вариант товара.", valueType: "object?" },
    ],
    outputs: [
      { id: "out-response", label: "buyerResponse.*", description: "Сформированный ответ покупателю.", valueType: "object" },
      { id: "out-next", label: "next", description: "Выход потока.", valueType: "flow" },
    ],
  },
  Notify: {
    inputs: [
      { id: "in-flow", label: "flow", description: "Вход потока.", valueType: "flow" },
      { id: "in-message", label: "message", description: "Текст уведомления.", valueType: "string" },
    ],
    outputs: [
      { id: "out-notify", label: "notify.*", description: "Результат уведомления.", valueType: "object" },
      { id: "out-next", label: "next", description: "Выход потока.", valueType: "flow" },
    ],
  },
  End: {
    inputs: [{ id: "in-flow", label: "flow", description: "Вход потока завершения.", valueType: "flow" }],
    outputs: [],
  },
};

const nodeFieldDescriptors: Record<WorkflowNode["type"], readonly WorkflowFieldDescriptor[]> = {
  PurchaseStart: [],
  MessageStart: [],
  ReviewStart: [],
  Condition: [
    {
      key: "field",
      label: "field",
      description: "Имя переменной в runtime-контексте (например: platform).",
      required: true,
      type: "string",
      placeholder: "platform",
    },
    {
      key: "equals",
      label: "equals",
      description: "Значение для сравнения по equals (например: steam).",
      required: true,
      type: "string",
      placeholder: "steam",
    },
  ],
  SetVariables: [
    {
      key: "values",
      label: "values",
      description: "JSON-объект ключей/значений, которые нужно положить в runtime-контекст.",
      required: false,
      type: "json-object",
    },
  ],
  LoadOffer: [],
  SelectAccountPriorityFallback: [
    {
      key: "platform",
      label: "platform",
      description: "Опциональный фильтр платформы для приоритетного выбора варианта.",
      required: false,
      type: "string",
      placeholder: "steam",
    },
  ],
  InvokeWorkerAction: [
    {
      key: "action",
      label: "action",
      description: "Action key воркера, например: ext.integration.steam.jobs.",
      required: false,
      type: "string",
      placeholder: "ext.integration.steam.jobs",
    },
  ],
  InvokeCustomHttp: [
    {
      key: "integrationId",
      label: "integrationId",
      description: "GUID custom HTTP integration проекта.",
      required: true,
      type: "guid",
      placeholder: "00000000-0000-0000-0000-000000000000",
    },
    {
      key: "method",
      label: "method",
      description: "HTTP метод (GET/POST/PUT/PATCH/DELETE). По умолчанию POST.",
      required: false,
      type: "string",
      placeholder: "POST",
    },
    {
      key: "path",
      label: "path",
      description: "Опциональный путь, добавляется к baseUrl интеграции.",
      required: false,
      type: "string",
      placeholder: "/orders/fulfill",
    },
    {
      key: "headers",
      label: "headers",
      description: "Опциональный JSON-объект заголовков string->string.",
      required: false,
      type: "json-object",
    },
    {
      key: "payload",
      label: "payload",
      description: "Опциональный JSON-объект payload для запроса.",
      required: false,
      type: "json-object",
    },
  ],
  SteamAction: [
    {
      key: "action",
      label: "action",
      description: "Тип steam-задачи, например: change-password, relogin, refresh-cookies.",
      required: true,
      type: "string",
      placeholder: "change-password",
    },
    {
      key: "accountId",
      label: "accountId",
      description: "GUID аккаунта Steam для точечной задачи (если нужен).",
      required: false,
      type: "guid",
      placeholder: "00000000-0000-0000-0000-000000000000",
    },
    {
      key: "delaySeconds",
      label: "delaySeconds",
      description: "Опциональная задержка запуска Steam action в секундах.",
      required: false,
      type: "int",
      placeholder: "10800",
    },
    {
      key: "payload",
      label: "payload",
      description: "Дополнительные параметры Steam action в JSON-объекте.",
      required: false,
      type: "json-object",
    },
  ],
  Task: [
    {
      key: "taskType",
      label: "taskType",
      description: "Ключ бизнес-задачи, например: steam.change-password.",
      required: true,
      type: "string",
      placeholder: "steam.change-password",
    },
    {
      key: "title",
      label: "title",
      description: "Короткий заголовок задачи для команды/оператора.",
      required: false,
      type: "string",
      placeholder: "Сменить пароль на Steam",
    },
    {
      key: "assignee",
      label: "assignee",
      description: "Исполнитель задачи (логин/ID/группа).",
      required: false,
      type: "string",
      placeholder: "support-team",
    },
    {
      key: "delaySeconds",
      label: "delaySeconds",
      description: "Отложенный старт задачи в секундах (например 10800 = 3 часа).",
      required: false,
      type: "int",
      placeholder: "10800",
    },
    {
      key: "payload",
      label: "payload",
      description: "JSON-данные задачи (контекст, параметры, комментарии).",
      required: false,
      type: "json-object",
    },
  ],
  SendBuyerResponse: [
    {
      key: "message",
      label: "message",
      description: "Шаблон сообщения покупателю; поддерживает токены selectedVariant.*.",
      required: false,
      type: "string",
      placeholder: "Ваш товар: {{selectedVariant.workerProductId}}",
    },
  ],
  Notify: [
    {
      key: "message",
      label: "message",
      description: "Текст служебного уведомления для notify node.",
      required: false,
      type: "string",
      placeholder: "Сделка обработана",
    },
  ],
  End: [],
};

interface WorkflowPreset {
  id: string;
  label: string;
  description: string;
  draft: WorkflowDraft;
}

const workflowPresets: readonly WorkflowPreset[] = [
  {
    id: "purchase-basic",
    label: "Purchase: basic delivery",
    description: "Старт покупки -> Load Offer -> Select Account -> Buyer Response -> End.",
    draft: {
      version: "v1",
      maxSteps: 128,
      maxDurationSeconds: 120,
      maxRetries: 3,
      nodes: [
        {
          id: "purchase-start",
          type: "PurchaseStart",
          name: "Покупка",
          config: {},
          ui: { position: { x: 80, y: 140 } },
        },
        {
          id: "load-offer",
          type: "LoadOffer",
          name: "Загрузить оффер",
          config: {},
          ui: { position: { x: 420, y: 140 } },
        },
        {
          id: "select-account",
          type: "SelectAccountPriorityFallback",
          name: "Выбрать аккаунт",
          config: {},
          ui: { position: { x: 760, y: 140 } },
        },
        {
          id: "send-buyer-response",
          type: "SendBuyerResponse",
          name: "Ответ покупателю",
          config: {
            message: "Данные аккаунта: {{selectedVariant.workerProductId}}",
          },
          ui: { position: { x: 1100, y: 140 } },
        },
        {
          id: "end",
          type: "End",
          name: "Finish",
          config: {},
          ui: { position: { x: 1440, y: 140 } },
        },
      ],
      edges: [
        {
          id: "edge-1",
          source: "purchase-start",
          sourceHandle: "out-flow",
          target: "load-offer",
          targetHandle: "in-flow",
        },
        {
          id: "edge-2",
          source: "load-offer",
          sourceHandle: "out-next",
          target: "select-account",
          targetHandle: "in-flow",
        },
        {
          id: "edge-3",
          source: "select-account",
          sourceHandle: "out-next",
          target: "send-buyer-response",
          targetHandle: "in-flow",
        },
        {
          id: "edge-4",
          source: "send-buyer-response",
          sourceHandle: "out-next",
          target: "end",
          targetHandle: "in-flow",
        },
      ],
      ui: {
        entryNodeId: "purchase-start",
        viewport: {
          x: -40,
          y: 0,
          zoom: 0.8,
        },
      },
    },
  },
  {
    id: "purchase-http-fulfillment",
    label: "Purchase: HTTP fulfill + notify",
    description: "Старт покупки -> Load Offer -> Select Account -> Invoke HTTP -> Notify -> Buyer Response -> End.",
    draft: {
      version: "v1",
      maxSteps: 160,
      maxDurationSeconds: 180,
      maxRetries: 3,
      nodes: [
        {
          id: "purchase-start",
          type: "PurchaseStart",
          name: "Покупка",
          config: {},
          ui: { position: { x: 80, y: 140 } },
        },
        {
          id: "load-offer",
          type: "LoadOffer",
          name: "Load Offer",
          config: {},
          ui: { position: { x: 420, y: 140 } },
        },
        {
          id: "select-account",
          type: "SelectAccountPriorityFallback",
          name: "Select Account",
          config: {},
          ui: { position: { x: 760, y: 140 } },
        },
        {
          id: "invoke-http",
          type: "InvokeCustomHttp",
          name: "Fulfillment HTTP",
          config: {
            method: "POST",
            path: "/orders/fulfill",
            payload: {},
          },
          ui: { position: { x: 1100, y: 140 } },
        },
        {
          id: "notify",
          type: "Notify",
          name: "Notify team",
          config: {
            message: "order={{purchase.sourceOrderId}} fulfilled",
          },
          ui: { position: { x: 1440, y: 60 } },
        },
        {
          id: "send-buyer-response",
          type: "SendBuyerResponse",
          name: "Buyer message",
          config: {
            message: "Заказ {{purchase.sourceOrderId}} обработан.",
          },
          ui: { position: { x: 1440, y: 220 } },
        },
        {
          id: "end",
          type: "End",
          name: "Finish",
          config: {},
          ui: { position: { x: 1760, y: 140 } },
        },
      ],
      edges: [
        {
          id: "edge-1",
          source: "purchase-start",
          sourceHandle: "out-flow",
          target: "load-offer",
          targetHandle: "in-flow",
        },
        {
          id: "edge-2",
          source: "load-offer",
          sourceHandle: "out-next",
          target: "select-account",
          targetHandle: "in-flow",
        },
        {
          id: "edge-3",
          source: "select-account",
          sourceHandle: "out-next",
          target: "invoke-http",
          targetHandle: "in-flow",
        },
        {
          id: "edge-4",
          source: "invoke-http",
          sourceHandle: "out-next",
          target: "notify",
          targetHandle: "in-flow",
        },
        {
          id: "edge-5",
          source: "invoke-http",
          sourceHandle: "out-next",
          target: "send-buyer-response",
          targetHandle: "in-flow",
        },
        {
          id: "edge-6",
          source: "notify",
          sourceHandle: "out-next",
          target: "end",
          targetHandle: "in-flow",
        },
        {
          id: "edge-7",
          source: "send-buyer-response",
          sourceHandle: "out-next",
          target: "end",
          targetHandle: "in-flow",
        },
      ],
      ui: {
        entryNodeId: "purchase-start",
        viewport: {
          x: -80,
          y: 0,
          zoom: 0.72,
        },
      },
    },
  },
  {
    id: "message-support-auto-reply",
    label: "Message: support triage",
    description: "Старт сообщения -> Condition -> Buyer Response/Notify -> End.",
    draft: {
      version: "v1",
      maxSteps: 96,
      maxDurationSeconds: 120,
      maxRetries: 2,
      nodes: [
        {
          id: "message-start",
          type: "MessageStart",
          name: "Входящее сообщение",
          config: {},
          ui: { position: { x: 80, y: 180 } },
        },
        {
          id: "condition-message",
          type: "Condition",
          name: "Проверить команду",
          config: {
            field: "message.text",
            equals: "/status",
          },
          ui: { position: { x: 420, y: 180 } },
        },
        {
          id: "send-status",
          type: "SendBuyerResponse",
          name: "Ответ по статусу",
          config: {
            message: "Заказ {{purchase.sourceOrderId}} в обработке.",
          },
          ui: { position: { x: 760, y: 90 } },
        },
        {
          id: "notify-support",
          type: "Notify",
          name: "Передать в поддержку",
          config: {
            message: "Требуется ответ оператора: {{message.text}}",
          },
          ui: { position: { x: 760, y: 270 } },
        },
        {
          id: "end",
          type: "End",
          name: "Finish",
          config: {},
          ui: { position: { x: 1090, y: 180 } },
        },
      ],
      edges: [
        {
          id: "edge-1",
          source: "message-start",
          sourceHandle: "out-flow",
          target: "condition-message",
          targetHandle: "in-flow",
        },
        {
          id: "edge-2",
          source: "condition-message",
          sourceHandle: "out-true",
          target: "send-status",
          targetHandle: "in-flow",
        },
        {
          id: "edge-3",
          source: "condition-message",
          sourceHandle: "out-false",
          target: "notify-support",
          targetHandle: "in-flow",
        },
        {
          id: "edge-4",
          source: "send-status",
          sourceHandle: "out-next",
          target: "end",
          targetHandle: "in-flow",
        },
        {
          id: "edge-5",
          source: "notify-support",
          sourceHandle: "out-next",
          target: "end",
          targetHandle: "in-flow",
        },
      ],
      ui: {
        entryNodeId: "message-start",
        viewport: {
          x: -40,
          y: 0,
          zoom: 0.88,
        },
      },
    },
  },
  {
    id: "review-negative-followup-task",
    label: "Review: negative follow-up",
    description: "Старт отзыва -> Condition -> Task + Notify -> End.",
    draft: {
      version: "v1",
      maxSteps: 120,
      maxDurationSeconds: 180,
      maxRetries: 3,
      nodes: [
        {
          id: "review-start",
          type: "ReviewStart",
          name: "Новый отзыв",
          config: {},
          ui: { position: { x: 80, y: 180 } },
        },
        {
          id: "condition-rating",
          type: "Condition",
          name: "Низкая оценка",
          config: {
            field: "review.rating",
            equals: "1",
          },
          ui: { position: { x: 420, y: 180 } },
        },
        {
          id: "task-followup",
          type: "Task",
          name: "Follow-up задача",
          config: {
            taskType: "support.review.followup",
            title: "Связаться с клиентом после негативного отзыва",
            assignee: "support-team",
            delaySeconds: 10800,
            payload: {
              priority: "high",
            },
          },
          ui: { position: { x: 760, y: 90 } },
        },
        {
          id: "notify-team",
          type: "Notify",
          name: "Уведомить команду",
          config: {
            message: "Негативный отзыв: {{review.text}}",
          },
          ui: { position: { x: 760, y: 260 } },
        },
        {
          id: "end",
          type: "End",
          name: "Finish",
          config: {},
          ui: { position: { x: 1080, y: 180 } },
        },
      ],
      edges: [
        {
          id: "edge-1",
          source: "review-start",
          sourceHandle: "out-flow",
          target: "condition-rating",
          targetHandle: "in-flow",
        },
        {
          id: "edge-2",
          source: "condition-rating",
          sourceHandle: "out-true",
          target: "task-followup",
          targetHandle: "in-flow",
        },
        {
          id: "edge-3",
          source: "condition-rating",
          sourceHandle: "out-false",
          target: "end",
          targetHandle: "in-flow",
        },
        {
          id: "edge-4",
          source: "task-followup",
          sourceHandle: "out-next",
          target: "notify-team",
          targetHandle: "in-flow",
        },
        {
          id: "edge-5",
          source: "notify-team",
          sourceHandle: "out-next",
          target: "end",
          targetHandle: "in-flow",
        },
      ],
      ui: {
        entryNodeId: "review-start",
        viewport: {
          x: -30,
          y: 0,
          zoom: 0.86,
        },
      },
    },
  },
];

function readFieldDescriptor(nodeType: WorkflowNode["type"], fieldKey: string) {
  return nodeFieldDescriptors[nodeType].find((field) => field.key === fieldKey) ?? null;
}

function readFieldTypeTitle(type: WorkflowFieldDescriptor["type"]) {
  if (type === "guid") {
    return "GUID";
  }

  if (type === "json-object") {
    return "JSON object";
  }

  if (type === "int") {
    return "integer";
  }

  return "string";
}

function readFieldHintText(nodeType: WorkflowNode["type"], fieldKey: string) {
  const descriptor = readFieldDescriptor(nodeType, fieldKey);
  if (!descriptor) {
    return "";
  }

  const requiredTitle = descriptor.required ? "Обязательно" : "Опционально";
  return `${requiredTitle} · ${readFieldTypeTitle(descriptor.type)}. ${descriptor.description}`;
}

function buildEmptyTypedEditorState(): WorkflowTypedEditorState {
  return {
    conditionField: "",
    conditionEquals: "",
    setVariablesText: "{}",
    selectPlatform: "",
    workerAction: "",
    customIntegrationId: "",
    customMethod: "POST",
    customPath: "",
    customHeadersText: "{}",
    customPayloadText: "{}",
    steamAction: "change-password",
    steamAccountId: "",
    steamDelaySeconds: "",
    steamPayloadText: "{}",
    taskType: "",
    taskTitle: "",
    taskAssignee: "",
    taskDelaySeconds: "",
    taskPayloadText: "{}",
    buyerMessage: "",
    notifyMessage: "",
  };
}

function generateClientId(prefix: string) {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return `${prefix}-${crypto.randomUUID()}`;
  }

  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 100_000)}`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

function readNodeConfig(value: unknown): Record<string, unknown> {
  if (!isRecord(value)) {
    return {};
  }

  return { ...value };
}

function readFiniteNumber(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return null;
  }

  return value;
}

function normalizeViewportInput(viewport: WorkflowDraftUi["viewport"] | undefined | null): WorkflowDraftUi["viewport"] | undefined {
  if (!viewport) {
    return undefined;
  }

  const x = readFiniteNumber(viewport.x);
  const y = readFiniteNumber(viewport.y);
  const zoom = readFiniteNumber(viewport.zoom);
  if (x === null || y === null || zoom === null) {
    return undefined;
  }

  return { x, y, zoom };
}

function isSameViewport(left: WorkflowDraftUi["viewport"] | undefined, right: WorkflowDraftUi["viewport"] | undefined) {
  if (!left && !right) {
    return true;
  }

  if (!left || !right) {
    return false;
  }

  return Math.abs(left.x - right.x) < 0.001
    && Math.abs(left.y - right.y) < 0.001
    && Math.abs(left.zoom - right.zoom) < 0.0001;
}

function buildAutoPosition(index: number) {
  const column = index % 4;
  const row = Math.floor(index / 4);

  return {
    x: 80 + column * 280,
    y: 80 + row * 170,
  };
}

interface WorkflowGraphNodeLike {
  id: string;
}

interface WorkflowGraphEdgeLike {
  source: string;
  target: string;
}

function listRootNodeIds<TNode extends WorkflowGraphNodeLike, TEdge extends WorkflowGraphEdgeLike>(nodes: TNode[], edges: TEdge[]) {
  const incomingNodeIds = new Set(edges.map((edge) => edge.target.trim()));
  return nodes
    .map((node) => node.id.trim())
    .filter((nodeId) => nodeId.length > 0 && !incomingNodeIds.has(nodeId))
    .sort((left, right) => left.localeCompare(right));
}

function resolveEntryNodeId<TNode extends WorkflowGraphNodeLike, TEdge extends WorkflowGraphEdgeLike>(
  nodes: TNode[],
  edges: TEdge[],
  requestedEntryNodeId: string | undefined,
) {
  const rootNodeIds = listRootNodeIds(nodes, edges);
  if (rootNodeIds.length === 0) {
    return "";
  }

  const normalizedRequested = requestedEntryNodeId?.trim() ?? "";
  if (normalizedRequested.length > 0 && rootNodeIds.includes(normalizedRequested)) {
    return normalizedRequested;
  }

  return rootNodeIds[0];
}

function readNodeTypeFromGraphNode(node: unknown): WorkflowNode["type"] | null {
  if (!isRecord(node)) {
    return null;
  }

  const rawNodeType = node.type;
  if (typeof rawNodeType === "string" && workflowNodeTypes.some((type) => type === rawNodeType)) {
    return rawNodeType as WorkflowNode["type"];
  }

  const data = isRecord(node.data) ? node.data : null;
  const rawDataNodeType = data?.nodeType;
  if (typeof rawDataNodeType === "string" && workflowNodeTypes.some((type) => type === rawDataNodeType)) {
    return rawDataNodeType as WorkflowNode["type"];
  }

  return null;
}

function resolveStartEntryNodeId<TNode extends WorkflowGraphNodeLike, TEdge extends WorkflowGraphEdgeLike>(
  nodes: TNode[],
  edges: TEdge[],
  requestedEntryNodeId: string | undefined,
) {
  const rootNodeIds = listRootNodeIds(nodes, edges);
  if (rootNodeIds.length === 0) {
    return "";
  }

  const startRootIds = rootNodeIds.filter((nodeId) => {
    const node = nodes.find((candidate) => candidate.id.trim() === nodeId);
    const nodeType = readNodeTypeFromGraphNode(node);
    return nodeType ? workflowStartNodeTypes.includes(nodeType) : false;
  });

  if (startRootIds.length > 0) {
    const requestedId = requestedEntryNodeId?.trim() ?? "";
    if (requestedId.length > 0 && startRootIds.includes(requestedId)) {
      return requestedId;
    }

    const purchaseRoot = startRootIds.find((nodeId) => {
      const node = nodes.find((candidate) => candidate.id.trim() === nodeId);
      return readNodeTypeFromGraphNode(node) === "PurchaseStart";
    });
    return purchaseRoot ?? startRootIds[0];
  }

  return resolveEntryNodeId(nodes, edges, requestedEntryNodeId);
}

function toEditorNodes(nodes: WorkflowNode[], entryNodeId: string): Node<WorkflowEditorNodeData>[] {
  return nodes.map((node, index) => {
    const fallbackPosition = buildAutoPosition(index);
    const storedPositionX = readFiniteNumber(node.ui?.position?.x);
    const storedPositionY = readFiniteNumber(node.ui?.position?.y);
    const name = node.name ?? "";

    return {
      id: node.id,
      type: "workflowNode",
      position: {
        x: storedPositionX ?? fallbackPosition.x,
        y: storedPositionY ?? fallbackPosition.y,
      },
      data: {
        nodeType: node.type,
        name,
        config: readNodeConfig(node.config),
        isEntry: node.id === entryNodeId,
      },
    };
  });
}

function toEditorEdges(edges: WorkflowDraft["edges"]): Edge<WorkflowEditorEdgeData>[] {
  const seenKeys = new Set<string>();
  const normalizedEdges: Edge<WorkflowEditorEdgeData>[] = [];

  for (const edge of edges) {
    const condition = edge.condition?.trim();
    const edgeKey = [
      edge.source.trim(),
      edge.sourceHandle?.trim() ?? "",
      edge.target.trim(),
      edge.targetHandle?.trim() ?? "",
      condition ?? "",
    ].join("|");

    if (seenKeys.has(edgeKey)) {
      continue;
    }
    seenKeys.add(edgeKey);

    normalizedEdges.push({
      id: edge.id,
      source: edge.source,
      sourceHandle: edge.sourceHandle,
      target: edge.target,
      targetHandle: edge.targetHandle,
      label: condition || undefined,
      data: {
        condition: condition ?? "",
      },
      type: "smoothstep",
    });
  }

  return normalizedEdges;
}

function sanitizeNodeConfig(config: Record<string, unknown>): Record<string, unknown> | undefined {
  const entries = Object.entries(config).filter(([, value]) => value !== undefined);
  if (entries.length === 0) {
    return undefined;
  }

  return Object.fromEntries(entries);
}

function buildWorkflowDraftFromEditor(params: {
  version: string;
  maxSteps: number;
  maxDurationSeconds: number;
  maxRetries: number;
  nodes: Node<WorkflowEditorNodeData>[];
  edges: Edge<WorkflowEditorEdgeData>[];
  viewport?: WorkflowDraftUi["viewport"];
  entryNodeId?: string;
}): WorkflowDraft {
  const draftNodes: WorkflowNode[] = params.nodes.map((node) => ({
    id: node.id,
    type: node.data.nodeType,
    name: node.data.name.trim() || undefined,
    config: sanitizeNodeConfig(node.data.config),
    ui: {
      position: {
        x: node.position.x,
        y: node.position.y,
      },
    },
  }));

  const edgeKeys = new Set<string>();
  const draftEdges = params.edges.flatMap((edge) => {
    const condition = (edge.data?.condition ?? "").trim();
    const edgeKey = [
      edge.source.trim(),
      edge.sourceHandle?.trim() ?? "",
      edge.target.trim(),
      edge.targetHandle?.trim() ?? "",
      condition,
    ].join("|");

    if (edgeKeys.has(edgeKey)) {
      return [];
    }
    edgeKeys.add(edgeKey);

    return [{
      id: edge.id,
      source: edge.source,
      sourceHandle: edge.sourceHandle ?? undefined,
      target: edge.target,
      targetHandle: edge.targetHandle ?? undefined,
      condition: condition || undefined,
    }];
  });

  return {
    version: params.version.trim() || "v1",
    nodes: draftNodes,
    edges: draftEdges,
    maxSteps: params.maxSteps,
    maxDurationSeconds: params.maxDurationSeconds,
    maxRetries: params.maxRetries,
    ui: params.viewport || params.entryNodeId
      ? {
        ...(params.viewport ? { viewport: params.viewport } : {}),
        ...(params.entryNodeId ? { entryNodeId: params.entryNodeId } : {}),
      }
      : undefined,
  };
}

function cloneDraft(draft: WorkflowDraft): WorkflowDraft {
  return JSON.parse(JSON.stringify(draft)) as WorkflowDraft;
}

function readIntegerStringOrDefault(value: unknown, fallback: string) {
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(Math.floor(value));
  }

  if (typeof value !== "string") {
    return fallback;
  }

  const normalized = value.trim();
  if (!/^[-]?\d+$/.test(normalized)) {
    return fallback;
  }

  return normalized;
}

function readDraftFromImportPayload(raw: unknown): WorkflowDraft {
  const payload = isRecord(raw) ? raw : {};
  const draftSource = isRecord(payload.draft) ? payload.draft : payload;

  if (!isRecord(draftSource)) {
    throw new Error("Файл должен содержать объект draft.");
  }

  const rawNodes = Array.isArray(draftSource.nodes) ? draftSource.nodes : [];
  const rawEdges = Array.isArray(draftSource.edges) ? draftSource.edges : [];
  const importedNodes: WorkflowNode[] = rawNodes.map((item) => {
    if (!isRecord(item)) {
      throw new Error("nodes[] должен быть объектом.");
    }

    if (typeof item.id !== "string" || item.id.trim().length === 0) {
      throw new Error("nodes[].id обязателен.");
    }

    const nodeType = readNodeTypeFromGraphNode(item);
    if (!nodeType) {
      throw new Error(`nodes[].type не поддерживается (${String(item.type)})`);
    }

    return {
      id: item.id.trim(),
      type: nodeType,
      name: typeof item.name === "string" ? item.name : undefined,
      config: readNodeConfig(item.config),
      ui: isRecord(item.ui) && isRecord(item.ui.position)
        ? {
          position: {
            x: typeof item.ui.position.x === "number" ? item.ui.position.x : 0,
            y: typeof item.ui.position.y === "number" ? item.ui.position.y : 0,
          },
        }
        : undefined,
    };
  });

  const importedEdges: WorkflowDraft["edges"] = rawEdges.map((item) => {
    if (!isRecord(item)) {
      throw new Error("edges[] должен быть объектом.");
    }

    if (typeof item.id !== "string" || typeof item.source !== "string" || typeof item.target !== "string") {
      throw new Error("edges[] должен содержать id/source/target.");
    }

    return {
      id: item.id.trim(),
      source: item.source.trim(),
      sourceHandle: typeof item.sourceHandle === "string" ? item.sourceHandle : undefined,
      target: item.target.trim(),
      targetHandle: typeof item.targetHandle === "string" ? item.targetHandle : undefined,
      condition: typeof item.condition === "string" ? item.condition : undefined,
    };
  });

  if (importedNodes.length === 0) {
    throw new Error("Импортированный flow пустой: нет nodes.");
  }

  return {
    version: typeof draftSource.version === "string" ? draftSource.version : "v1",
    maxSteps: typeof draftSource.maxSteps === "number" ? draftSource.maxSteps : 128,
    maxDurationSeconds: typeof draftSource.maxDurationSeconds === "number" ? draftSource.maxDurationSeconds : 120,
    maxRetries: typeof draftSource.maxRetries === "number" ? draftSource.maxRetries : 3,
    nodes: importedNodes,
    edges: importedEdges,
    ui: isRecord(draftSource.ui)
      ? {
        viewport: isRecord(draftSource.ui.viewport)
          ? {
            x: typeof draftSource.ui.viewport.x === "number" ? draftSource.ui.viewport.x : 0,
            y: typeof draftSource.ui.viewport.y === "number" ? draftSource.ui.viewport.y : 0,
            zoom: typeof draftSource.ui.viewport.zoom === "number" ? draftSource.ui.viewport.zoom : 1,
          }
          : undefined,
        entryNodeId: typeof draftSource.ui.entryNodeId === "string" ? draftSource.ui.entryNodeId : undefined,
      }
      : undefined,
  };
}

function normalizeOfferId(offers: Offer[], selectedOfferId: string) {
  if (selectedOfferId && offers.some((offer) => offer.id === selectedOfferId)) {
    return selectedOfferId;
  }

  return offers[0]?.id ?? "";
}

function isTypingTarget(target: EventTarget | null) {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  const tagName = target.tagName;
  return tagName === "INPUT" || tagName === "TEXTAREA" || tagName === "SELECT" || target.isContentEditable;
}

function readString(config: Record<string, unknown>, key: string) {
  const value = config[key];
  return typeof value === "string" ? value : "";
}

function stringifyConfigObject(value: unknown) {
  if (!isRecord(value)) {
    return "{}";
  }

  return JSON.stringify(value, null, 2);
}

function readNodeDisplayName(node: Node<WorkflowEditorNodeData>) {
  const trimmedName = node.data.name.trim();
  return trimmedName.length > 0 ? `${node.data.nodeType} (${trimmedName})` : `${node.data.nodeType} (${node.id})`;
}

function readPortTitle(port: WorkflowNodePort) {
  const typeTitle = port.valueType ? ` [${port.valueType}]` : "";
  return `${port.label}${typeTitle}: ${port.description}`;
}

function validateConfigValueType(
  node: Node<WorkflowEditorNodeData>,
  descriptor: WorkflowFieldDescriptor,
  value: unknown,
) {
  const nodeLabel = readNodeDisplayName(node);

  if (value === undefined) {
    if (descriptor.required) {
      throw new Error(`Node ${nodeLabel}: поле \`${descriptor.key}\` обязательно.`);
    }
    return;
  }

  if (descriptor.type === "string") {
    if (typeof value !== "string") {
      throw new Error(`Node ${nodeLabel}: поле \`${descriptor.key}\` должно быть строкой.`);
    }

    if (descriptor.required && value.trim().length === 0) {
      throw new Error(`Node ${nodeLabel}: поле \`${descriptor.key}\` не может быть пустым.`);
    }
    return;
  }

  if (descriptor.type === "guid") {
    if (typeof value !== "string" || !workflowNodeGuidRegex.test(value.trim())) {
      throw new Error(`Node ${nodeLabel}: поле \`${descriptor.key}\` должно быть валидным GUID.`);
    }
    return;
  }

  if (descriptor.type === "int") {
    if (typeof value !== "number" || !Number.isInteger(value)) {
      throw new Error(`Node ${nodeLabel}: поле \`${descriptor.key}\` должно быть целым числом.`);
    }

    if (value < 0) {
      throw new Error(`Node ${nodeLabel}: поле \`${descriptor.key}\` не может быть отрицательным.`);
    }
    return;
  }

  if (!isRecord(value)) {
    throw new Error(`Node ${nodeLabel}: поле \`${descriptor.key}\` должно быть JSON-объектом.`);
  }
}

function validateNodeConfig(node: Node<WorkflowEditorNodeData>) {
  const config = node.data.config;
  const descriptors = nodeFieldDescriptors[node.data.nodeType];
  for (const descriptor of descriptors) {
    validateConfigValueType(node, descriptor, config[descriptor.key]);
  }

  if (node.data.nodeType === "InvokeCustomHttp") {
    const methodRaw = config.method;
    if (typeof methodRaw === "string" && methodRaw.trim().length > 0) {
      const normalizedMethod = methodRaw.trim().toUpperCase();
      if (!workflowHttpMethods.some((method) => method === normalizedMethod)) {
        throw new Error(`Node ${readNodeDisplayName(node)}: method должен быть одним из ${workflowHttpMethods.join("/")}.`);
      }
    }
  }
}

function validateWorkflowDraftClient(params: {
  selectedOfferId: string;
  version: string;
  maxSteps: number;
  maxDurationSeconds: number;
  maxRetries: number;
  nodes: Node<WorkflowEditorNodeData>[];
  edges: Edge<WorkflowEditorEdgeData>[];
  entryNodeId: string;
  activeIntegrationKeys: ReadonlySet<string>;
}) {
  if (!params.selectedOfferId) {
    throw new Error("Сначала выберите Offer.");
  }

  if (!params.version.trim()) {
    throw new Error("Version обязателен.");
  }

  if (!Number.isInteger(params.maxSteps) || params.maxSteps < 1 || params.maxSteps > 5000) {
    throw new Error("Max steps должен быть целым числом в диапазоне 1..5000.");
  }

  if (!Number.isInteger(params.maxDurationSeconds) || params.maxDurationSeconds < 1 || params.maxDurationSeconds > 3600) {
    throw new Error("Max duration должен быть целым числом в диапазоне 1..3600.");
  }

  if (!Number.isInteger(params.maxRetries) || params.maxRetries < 0 || params.maxRetries > 20) {
    throw new Error("Max retries должен быть целым числом в диапазоне 0..20.");
  }

  if (params.nodes.length === 0) {
    throw new Error("Добавьте хотя бы один node.");
  }

  const rootNodeIds = listRootNodeIds(params.nodes, params.edges);
  if (rootNodeIds.length === 0) {
    throw new Error("Workflow должен содержать минимум один root-node без входящих ребер.");
  }

  if (!params.entryNodeId.trim()) {
    throw new Error("Выберите entry point workflow.");
  }

  if (!rootNodeIds.includes(params.entryNodeId.trim())) {
    throw new Error("Entry point должен быть root-node без входящих ребер.");
  }

  const startNodes = params.nodes.filter((node) => workflowStartNodeTypes.includes(node.data.nodeType));
  if (startNodes.length === 0) {
    throw new Error("Добавьте хотя бы одну стартовую node: Purchase/Message/Review.");
  }

  for (const nodeType of workflowStartNodeTypes) {
    const sameTypeNodes = startNodes.filter((node) => node.data.nodeType === nodeType);
    if (sameTypeNodes.length > 1) {
      throw new Error(`Допускается только одна стартовая node типа ${nodeType}.`);
    }
  }

  const entryNode = params.nodes.find((node) => node.id === params.entryNodeId.trim());
  if (!entryNode || !workflowStartNodeTypes.includes(entryNode.data.nodeType)) {
    throw new Error("Entry point должен указывать на стартовую node Purchase/Message/Review.");
  }

  const hasEndNode = params.nodes.some((node) => node.data.nodeType === "End");
  if (!hasEndNode) {
    throw new Error("Workflow должен содержать хотя бы один node типа End.");
  }

  for (const node of params.nodes) {
    const catalogNode = workflowNodeCatalogByType[node.data.nodeType];
    if (catalogNode.integrationKey && !params.activeIntegrationKeys.has(catalogNode.integrationKey)) {
      throw new Error(`Node ${readNodeDisplayName(node)} недоступен: интеграция \`${catalogNode.integrationKey}\` не активна в проекте.`);
    }
    validateNodeConfig(node);
  }
}

const WorkflowCanvasNode = memo(function WorkflowCanvasNode({ data, selected }: NodeProps<Node<WorkflowEditorNodeData>>) {
  const toneClass = `is-tone-${nodeToneByType[data.nodeType]}`;
  const ports = nodePortsByType[data.nodeType];
  const totalRows = Math.max(ports.inputs.length, ports.outputs.length, 1);

  return (
    <article className={`workflow-node-card ${toneClass} ${selected ? "is-selected" : ""}`}>
      <header>
        <strong>{nodeLabelByType[data.nodeType]}</strong>
        <div className="workflow-node-meta">
          {data.isEntry ? <span className="workflow-node-entry-badge">Entry</span> : null}
          <span>{data.nodeType}</span>
        </div>
      </header>
      {data.name.trim().length > 0 ? <p>{data.name.trim()}</p> : <p className="is-muted">Без названия</p>}

      <div className="workflow-node-port-grid">
        {Array.from({ length: totalRows }).map((_, index) => {
          const inputPort = ports.inputs[index];
          const outputPort = ports.outputs[index];
          return (
            <div key={`port-row-${index}`} className="workflow-node-port-row">
              <span className="workflow-node-port-label is-left">
                {inputPort ? (
                  <Handle
                    id={inputPort.id}
                    className="workflow-node-handle workflow-node-row-handle is-left"
                    type="target"
                    position={Position.Left}
                  />
                ) : null}
              </span>
              <span className="workflow-node-port-label is-right">
                {outputPort ? (
                  <Handle
                    id={outputPort.id}
                    className="workflow-node-handle workflow-node-row-handle is-right"
                    type="source"
                    position={Position.Right}
                  />
                ) : null}
              </span>
            </div>
          );
        })}
      </div>
    </article>
  );
});

const workflowNodeRenderers = {
  workflowNode: WorkflowCanvasNode,
};

export function ProjectWorkflowsPanel({ apiSession, projectId, currentRole }: ProjectWorkflowsPanelProps) {
  const queryClient = useQueryClient();
  const [selectedOfferId, setSelectedOfferId] = useState("");
  const [selectedNodeId, setSelectedNodeId] = useState("");
  const [selectedEdgeId, setSelectedEdgeId] = useState("");

  const [version, setVersion] = useState("v1");
  const [maxSteps, setMaxSteps] = useState("128");
  const [maxDurationSeconds, setMaxDurationSeconds] = useState("120");
  const [maxRetries, setMaxRetries] = useState("3");
  const [entryNodeId, setEntryNodeId] = useState("");
  const [status, setStatus] = useState("Выберите Offer и настройте блок-схему workflow.");
  const [selectedPresetId, setSelectedPresetId] = useState(workflowPresets[0]?.id ?? "");

  const [nodeSearch, setNodeSearch] = useState("");
  const [typedEditor, setTypedEditor] = useState<WorkflowTypedEditorState>(buildEmptyTypedEditorState());
  const [typedEditorError, setTypedEditorError] = useState("");
  const [advancedJsonText, setAdvancedJsonText] = useState("{}");
  const [advancedJsonError, setAdvancedJsonError] = useState("");
  const [selectedNodeConfigVersion, setSelectedNodeConfigVersion] = useState(0);
  const [isAdvancedJsonOpen, setIsAdvancedJsonOpen] = useState(false);

  const [isHistoryOpen, setIsHistoryOpen] = useState(false);
  const [isMiniMapVisible, setIsMiniMapVisible] = useState(false);
  const [flowInstance, setFlowInstance] = useState<ReactFlowInstance<Node<WorkflowEditorNodeData>, Edge<WorkflowEditorEdgeData>> | null>(null);
  const importFileInputRef = useRef<HTMLInputElement | null>(null);
  const previousOfferIdRef = useRef("");
  const viewportRef = useRef<WorkflowDraftUi["viewport"]>(undefined);
  const pendingViewportRef = useRef<WorkflowDraftUi["viewport"]>(undefined);

  const canManageWorkflows = hasPermission(currentRole, projectPermissions.workflowsManage);

  const [nodes, setNodes] = useState<Node<WorkflowEditorNodeData>[]>([]);
  const [edges, setEdges] = useEdgesState<Edge<WorkflowEditorEdgeData>>([]);

  const offersQuery = useQuery({
    queryKey: ["offers", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listOffersRequest(apiSession, projectId),
    staleTime: 10_000,
    gcTime: 60_000,
    notifyOnChangeProps: ["data", "error", "isPending", "isFetching"],
    enabled: canManageWorkflows,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });
  const integrationsStatusQuery = useQuery({
    queryKey: ["project-integrations-status", apiSession.baseUrl, apiSession.token, projectId],
    queryFn: () => listProjectIntegrationsStatusRequest(apiSession, projectId),
    staleTime: 15_000,
    gcTime: 60_000,
    notifyOnChangeProps: ["data", "error", "isPending", "isFetching"],
    enabled: canManageWorkflows,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });

  const offers = useMemo(() => offersQuery.data ?? [], [offersQuery.data]);
  const activeIntegrationKeys = useMemo(
    () => new Set(
      (integrationsStatusQuery.data?.items ?? [])
        .filter((item) => item.status === "active")
        .map((item) => item.integrationKey),
    ),
    [integrationsStatusQuery.data],
  );

  useEffect(() => {
    if (!canManageWorkflows) {
      return;
    }

    const nextOfferId = normalizeOfferId(offers, selectedOfferId);
    if (nextOfferId !== selectedOfferId) {
      setSelectedOfferId(nextOfferId);
      setSelectedNodeId("");
      setSelectedEdgeId("");
      setEntryNodeId("");
    }

    if (!nextOfferId) {
      setNodes([]);
      setEdges([]);
      setEntryNodeId("");
    }
  }, [canManageWorkflows, offers, selectedOfferId, setEdges, setNodes]);

  const draftQuery = useQuery({
    queryKey: ["offer-workflow-draft", apiSession.baseUrl, apiSession.token, projectId, selectedOfferId],
    queryFn: () => getOfferWorkflowDraftRequest(apiSession, projectId, selectedOfferId),
    enabled: canManageWorkflows && selectedOfferId.length > 0,
    staleTime: 5_000,
    gcTime: 30_000,
    notifyOnChangeProps: ["data", "error", "isPending", "isFetching"],
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });

  const executionsQuery = useQuery({
    queryKey: ["offer-workflow-executions", apiSession.baseUrl, apiSession.token, projectId, selectedOfferId],
    queryFn: () => listOfferWorkflowExecutionsRequest(apiSession, projectId, selectedOfferId),
    enabled: canManageWorkflows && selectedOfferId.length > 0 && isHistoryOpen,
    staleTime: 5_000,
    gcTime: 30_000,
    notifyOnChangeProps: ["data", "error", "isPending", "isFetching"],
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });

  useEffect(() => {
    const previousOfferId = previousOfferIdRef.current;
    if (previousOfferId && previousOfferId !== selectedOfferId) {
      queryClient.removeQueries({
        queryKey: ["offer-workflow-draft", apiSession.baseUrl, apiSession.token, projectId, previousOfferId],
        exact: true,
      });
      queryClient.removeQueries({
        queryKey: ["offer-workflow-executions", apiSession.baseUrl, apiSession.token, projectId, previousOfferId],
        exact: true,
      });
    }

    previousOfferIdRef.current = selectedOfferId;
  }, [apiSession.baseUrl, apiSession.token, projectId, queryClient, selectedOfferId]);

  useEffect(() => () => {
    queryClient.removeQueries({
      queryKey: ["offer-workflow-draft", apiSession.baseUrl, apiSession.token, projectId],
    });
    queryClient.removeQueries({
      queryKey: ["offer-workflow-executions", apiSession.baseUrl, apiSession.token, projectId],
    });
  }, [apiSession.baseUrl, apiSession.token, projectId, queryClient]);

  const readViewportForSave = useCallback(() => {
    const flowViewport = flowInstance?.getViewport();
    return normalizeViewportInput(flowViewport ?? viewportRef.current);
  }, [flowInstance]);

  const applyDraftToEditor = useCallback((draft: WorkflowDraft) => {
    setVersion(draft.version);
    setMaxSteps(readIntegerStringOrDefault(draft.maxSteps, "128"));
    setMaxDurationSeconds(readIntegerStringOrDefault(draft.maxDurationSeconds, "120"));
    setMaxRetries(readIntegerStringOrDefault(draft.maxRetries, "3"));

    const nextEntryNodeId = resolveStartEntryNodeId(draft.nodes, draft.edges, draft.ui?.entryNodeId);
    const nextNodes = toEditorNodes(draft.nodes, nextEntryNodeId);
    const nextEdges = toEditorEdges(draft.edges);
    const droppedDuplicateEdges = Math.max(0, draft.edges.length - nextEdges.length);
    setNodes(nextNodes);
    setEdges(nextEdges);
    setEntryNodeId(nextEntryNodeId);
    const nextViewport = normalizeViewportInput(draft.ui?.viewport);
    pendingViewportRef.current = nextViewport;
    viewportRef.current = nextViewport;
    setSelectedNodeConfigVersion((current) => current + 1);

    setSelectedNodeId((previous) => {
      if (nextNodes.some((node) => node.id === previous)) {
        return previous;
      }

      setSelectedEdgeId("");
      return nextNodes[0]?.id ?? "";
    });

    if (droppedDuplicateEdges > 0) {
      setStatus(`Draft загружен. Удалено дублирующихся связей: ${droppedDuplicateEdges}.`);
    }
  }, [setEdges, setNodes]);

  const applyPreset = useCallback((presetId: string) => {
    const preset = workflowPresets.find((item) => item.id === presetId);
    if (!preset) {
      setStatus("Preset не найден.");
      return;
    }

    const presetDraft = cloneDraft(preset.draft);
    applyDraftToEditor(presetDraft);
    setSelectedPresetId(presetId);
    setStatus(`Применен preset: ${preset.label}.`);
  }, [applyDraftToEditor]);

  const exportFlow = useCallback(() => {
    try {
      const parsedMaxSteps = Number(maxSteps);
      const parsedDuration = Number(maxDurationSeconds);
      const parsedRetries = Number(maxRetries);
      const normalizedEntryNodeId = resolveStartEntryNodeId(nodes, edges, entryNodeId).trim();

      const exportDraft = buildWorkflowDraftFromEditor({
        version,
        maxSteps: Number.isFinite(parsedMaxSteps) ? Math.floor(parsedMaxSteps) : 128,
        maxDurationSeconds: Number.isFinite(parsedDuration) ? Math.floor(parsedDuration) : 120,
        maxRetries: Number.isFinite(parsedRetries) ? Math.floor(parsedRetries) : 3,
        nodes,
        edges,
        viewport: readViewportForSave(),
        entryNodeId: normalizedEntryNodeId || undefined,
      });

      const blob = new Blob([JSON.stringify(exportDraft, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `workflow-${selectedOfferId || "draft"}.json`;
      document.body.appendChild(anchor);
      anchor.click();
      document.body.removeChild(anchor);
      URL.revokeObjectURL(url);
      setStatus("Flow экспортирован в JSON.");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Не удалось экспортировать flow.");
    }
  }, [edges, entryNodeId, maxDurationSeconds, maxRetries, maxSteps, nodes, readViewportForSave, selectedOfferId, version]);

  const openImportDialog = useCallback(() => {
    importFileInputRef.current?.click();
  }, []);

  const importFlowFromFile = useCallback(async (file: File) => {
    const rawText = await file.text();
    const parsed = JSON.parse(rawText);
    const importedDraft = readDraftFromImportPayload(parsed);
    applyDraftToEditor(importedDraft);
    setStatus(`Flow импортирован из файла ${file.name}.`);
  }, [applyDraftToEditor]);

  useEffect(() => {
    if (!draftQuery.data) {
      return;
    }

    applyDraftToEditor(draftQuery.data.draft);
  }, [applyDraftToEditor, draftQuery.data]);

  useEffect(() => {
    if (!flowInstance || nodes.length === 0) {
      return;
    }

    const pendingViewport = pendingViewportRef.current;
    pendingViewportRef.current = undefined;

    if (pendingViewport) {
      void flowInstance.setViewport(pendingViewport, { duration: 0 });
      viewportRef.current = pendingViewport;
      return;
    }

    void flowInstance.fitView({ padding: 0.2, duration: 0 }).then(() => {
      viewportRef.current = normalizeViewportInput(flowInstance.getViewport());
    });
  }, [flowInstance, nodes]);

  const selectedNode = useMemo(
    () => nodes.find((node) => node.id === selectedNodeId) ?? null,
    [nodes, selectedNodeId],
  );

  const selectedEdge = useMemo(
    () => edges.find((edge) => edge.id === selectedEdgeId) ?? null,
    [edges, selectedEdgeId],
  );
  const rootNodeIds = useMemo(() => listRootNodeIds(nodes, edges), [nodes, edges]);
  const startRootNodeIds = useMemo(
    () => rootNodeIds.filter((nodeId) => nodes.some((node) => node.id === nodeId && workflowStartNodeTypes.includes(node.data.nodeType))),
    [nodes, rootNodeIds],
  );
  const selectedNodeIsStartRoot = useMemo(
    () => (selectedNode ? startRootNodeIds.includes(selectedNode.id) : false),
    [selectedNode, startRootNodeIds],
  );

  useEffect(() => {
    if (nodes.length === 0) {
      if (entryNodeId) {
        setEntryNodeId("");
      }
      return;
    }

    const nextEntryNodeId = resolveStartEntryNodeId(nodes, edges, entryNodeId);
    const shouldSyncEntry = nextEntryNodeId !== entryNodeId;
    const shouldSyncBadges = nodes.some((node) => node.data.isEntry !== (node.id === nextEntryNodeId));

    if (!shouldSyncEntry && !shouldSyncBadges) {
      return;
    }

    if (shouldSyncEntry) {
      setEntryNodeId(nextEntryNodeId);
    }

    setNodes((current) => current.map((node) => {
      const isEntry = node.id === nextEntryNodeId;
      if (node.data.isEntry === isEntry) {
        return node;
      }

      return {
        ...node,
        data: {
          ...node.data,
          isEntry,
        },
      };
    }));
  }, [edges, entryNodeId, nodes, setNodes]);

  useEffect(() => {
    if (!selectedNode) {
      setTypedEditor(buildEmptyTypedEditorState());
      setAdvancedJsonText("{}");
      setIsAdvancedJsonOpen(false);
      setTypedEditorError("");
      setAdvancedJsonError("");
      return;
    }

    const nextTypedEditor = buildEmptyTypedEditorState();
    const config = selectedNode.data.config;

    if (selectedNode.data.nodeType === "Condition") {
      nextTypedEditor.conditionField = readString(config, "field");
      nextTypedEditor.conditionEquals = readString(config, "equals");
    } else if (selectedNode.data.nodeType === "SetVariables") {
      nextTypedEditor.setVariablesText = stringifyConfigObject(config.values);
    } else if (selectedNode.data.nodeType === "SelectAccountPriorityFallback") {
      nextTypedEditor.selectPlatform = readString(config, "platform");
    } else if (selectedNode.data.nodeType === "InvokeWorkerAction") {
      nextTypedEditor.workerAction = readString(config, "action");
    } else if (selectedNode.data.nodeType === "InvokeCustomHttp") {
      nextTypedEditor.customIntegrationId = readString(config, "integrationId");
      nextTypedEditor.customMethod = readString(config, "method") || "POST";
      nextTypedEditor.customPath = readString(config, "path");
      nextTypedEditor.customHeadersText = stringifyConfigObject(config.headers);
      nextTypedEditor.customPayloadText = stringifyConfigObject(config.payload);
    } else if (selectedNode.data.nodeType === "SteamAction") {
      nextTypedEditor.steamAction = readString(config, "action") || "change-password";
      nextTypedEditor.steamAccountId = readString(config, "accountId");
      nextTypedEditor.steamDelaySeconds = typeof config.delaySeconds === "number" ? String(config.delaySeconds) : readString(config, "delaySeconds");
      nextTypedEditor.steamPayloadText = stringifyConfigObject(config.payload);
    } else if (selectedNode.data.nodeType === "Task") {
      nextTypedEditor.taskType = readString(config, "taskType");
      nextTypedEditor.taskTitle = readString(config, "title");
      nextTypedEditor.taskAssignee = readString(config, "assignee");
      nextTypedEditor.taskDelaySeconds = typeof config.delaySeconds === "number" ? String(config.delaySeconds) : readString(config, "delaySeconds");
      nextTypedEditor.taskPayloadText = stringifyConfigObject(config.payload);
    } else if (selectedNode.data.nodeType === "SendBuyerResponse") {
      nextTypedEditor.buyerMessage = readString(config, "message");
    } else if (selectedNode.data.nodeType === "Notify") {
      nextTypedEditor.notifyMessage = readString(config, "message");
    }

    setTypedEditor(nextTypedEditor);
    setAdvancedJsonText("");
    setIsAdvancedJsonOpen(false);
    setTypedEditorError("");
    setAdvancedJsonError("");
  }, [selectedNode?.id, selectedNode?.data.nodeType, selectedNodeConfigVersion]);

  const onNodesChange = useCallback((changes: NodeChange<Node<WorkflowEditorNodeData>>[]) => {
    const nonSelectionChanges = changes.filter((change) => change.type !== "select");
    if (nonSelectionChanges.length === 0) {
      return;
    }

    setNodes((current) => applyNodeChanges(nonSelectionChanges, current));
  }, [setNodes]);

  const onConnect = useCallback((connection: Connection) => {
    if (!connection.source || !connection.target) {
      return;
    }

    setEdges((current) => addEdge({
      ...connection,
      sourceHandle: connection.sourceHandle ?? undefined,
      targetHandle: connection.targetHandle ?? undefined,
      id: generateClientId("edge"),
      data: {
        condition: "",
      },
      type: "smoothstep",
    }, current));
  }, [setEdges]);

  const onEdgesChange = useCallback((changes: EdgeChange<Edge<WorkflowEditorEdgeData>>[]) => {
    const nonSelectionChanges = changes.filter((change) => change.type !== "select");
    if (nonSelectionChanges.length === 0) {
      return;
    }

    setEdges((current) => applyEdgeChanges(nonSelectionChanges, current));
  }, [setEdges]);

  const handleCanvasMoveEnd = useCallback((_: unknown, nextViewport: NonNullable<WorkflowDraftUi["viewport"]>) => {
    const normalizedViewport = normalizeViewportInput(nextViewport);
    if (isSameViewport(viewportRef.current, normalizedViewport)) {
      return;
    }

    viewportRef.current = normalizedViewport;
  }, []);

  const handleCanvasNodeClick = useCallback((_: unknown, node: Node<WorkflowEditorNodeData>) => {
    setSelectedNodeId(node.id);
    setSelectedEdgeId("");
  }, []);

  const handleCanvasEdgeClick = useCallback((_: unknown, edge: Edge<WorkflowEditorEdgeData>) => {
    setSelectedEdgeId(edge.id);
    setSelectedNodeId("");
  }, []);

  const handleCanvasPaneClick = useCallback(() => {
    setSelectedNodeId("");
    setSelectedEdgeId("");
  }, []);

  const handleCenterCanvas = useCallback(() => {
    if (!flowInstance) {
      return;
    }

    void flowInstance.fitView({ padding: 0.2, duration: 200 });
    viewportRef.current = normalizeViewportInput(flowInstance.getViewport());
  }, [flowInstance]);

  const defaultEdgeOptions = useMemo<DefaultEdgeOptions>(() => ({
    type: "smoothstep",
    interactionWidth: 12,
  }), []);

  const appendNode = (nodeType: WorkflowNode["type"]) => {
    const catalogNode = workflowNodeCatalogByType[nodeType];
    if (catalogNode.integrationKey && !activeIntegrationKeys.has(catalogNode.integrationKey)) {
      setStatus(`Node ${catalogNode.label} недоступен: активируйте интеграцию \`${catalogNode.integrationKey}\`.`);
      return;
    }

    if (workflowStartNodeTypes.includes(nodeType) && nodes.some((node) => node.data.nodeType === nodeType)) {
      setStatus(`Стартовая node ${nodeType} уже добавлена.`);
      return;
    }

    const position = buildAutoPosition(nodes.length);
    const nextNode: Node<WorkflowEditorNodeData> = {
      id: generateClientId("node"),
      type: "workflowNode",
      position,
      data: {
        nodeType,
        name: "",
        config: {},
        isEntry: false,
      },
    };

    setNodes((current) => [...current, nextNode]);
    setSelectedNodeId(nextNode.id);
    setSelectedEdgeId("");
    if (flowInstance) {
      void flowInstance.setCenter(position.x, position.y, {
        zoom: 1.15,
        duration: 180,
      });
      viewportRef.current = normalizeViewportInput(flowInstance.getViewport());
    }
    setStatus(`Node ${nodeType} добавлен.`);
  };

  const removeSelectedNode = useCallback(() => {
    if (!selectedNodeId) {
      return;
    }

    setNodes((current) => current.filter((node) => node.id !== selectedNodeId));
    setEdges((current) => current.filter((edge) => edge.source !== selectedNodeId && edge.target !== selectedNodeId));
    setSelectedNodeId("");
    setSelectedEdgeId("");
    setStatus("Выбранный node удален.");
  }, [selectedNodeId, setEdges, setNodes]);

  const removeSelectedEdge = useCallback(() => {
    if (!selectedEdgeId) {
      return;
    }

    setEdges((current) => current.filter((edge) => edge.id !== selectedEdgeId));
    setSelectedEdgeId("");
    setStatus("Выбранный edge удален.");
  }, [selectedEdgeId, setEdges]);

  const setWorkflowEntryNode = useCallback((nodeId: string) => {
    const normalizedNodeId = nodeId.trim();
    if (!normalizedNodeId) {
      setStatus("Entry point не выбран.");
      return;
    }

    if (!rootNodeIds.includes(normalizedNodeId)) {
      setStatus("Entry point должен быть root-node без входящих ребер.");
      return;
    }

    const selectedEntryNode = nodes.find((node) => node.id === normalizedNodeId);
    if (!selectedEntryNode || !workflowStartNodeTypes.includes(selectedEntryNode.data.nodeType)) {
      setStatus("Entry point должен быть стартовой node (Purchase/Message/Review).");
      return;
    }

    setEntryNodeId(normalizedNodeId);
    setNodes((current) => current.map((node) => {
      const isEntry = node.id === normalizedNodeId;
      if (node.data.isEntry === isEntry) {
        return node;
      }

      return {
        ...node,
        data: {
          ...node.data,
          isEntry,
        },
      };
    }));
    setStatus("Entry point обновлен.");
  }, [nodes, rootNodeIds, setNodes]);

  const updateSelectedNodeMeta = (patch: Partial<Pick<WorkflowEditorNodeData, "nodeType" | "name">>) => {
    if (!selectedNodeId) {
      return;
    }

    setNodes((current) => current.map((node) => {
      if (node.id !== selectedNodeId) {
        return node;
      }

      const nodeType = patch.nodeType ?? node.data.nodeType;
      const name = patch.name ?? node.data.name;

      return {
        ...node,
        data: {
          ...node.data,
          ...patch,
          nodeType,
          name,
        },
      };
    }));
  };

  const applyConfigPatch = useCallback((baseConfig: Record<string, unknown>, updates: Record<string, unknown | undefined>) => {
    const nextConfig: Record<string, unknown> = { ...baseConfig };
    for (const [key, value] of Object.entries(updates)) {
      if (value === undefined || value === "") {
        delete nextConfig[key];
      } else {
        nextConfig[key] = value;
      }
    }

    return nextConfig;
  }, []);

  const buildConfigFromTypedEditor = useCallback((
    nodeType: WorkflowNode["type"],
    baseConfig: Record<string, unknown>,
  ) => {
    if (nodeType === "Condition") {
      return applyConfigPatch(baseConfig, {
        field: typedEditor.conditionField.trim() || undefined,
        equals: typedEditor.conditionEquals.trim() || undefined,
      });
    }

    if (nodeType === "SetVariables") {
      const values = typedEditor.setVariablesText.trim().length > 0 ? JSON.parse(typedEditor.setVariablesText) : undefined;
      if (values !== undefined && !isRecord(values)) {
        throw new Error("values должен быть JSON-объектом.");
      }

      return applyConfigPatch(baseConfig, { values });
    }

    if (nodeType === "SelectAccountPriorityFallback") {
      return applyConfigPatch(baseConfig, {
        platform: typedEditor.selectPlatform.trim() || undefined,
      });
    }

    if (nodeType === "InvokeWorkerAction") {
      return applyConfigPatch(baseConfig, {
        action: typedEditor.workerAction.trim() || undefined,
      });
    }

    if (nodeType === "InvokeCustomHttp") {
      const headers = typedEditor.customHeadersText.trim().length > 0 ? JSON.parse(typedEditor.customHeadersText) : undefined;
      if (headers !== undefined && !isRecord(headers)) {
        throw new Error("Headers должен быть JSON-объектом.");
      }

      const payload = typedEditor.customPayloadText.trim().length > 0 ? JSON.parse(typedEditor.customPayloadText) : undefined;
      if (payload !== undefined && !isRecord(payload)) {
        throw new Error("Payload должен быть JSON-объектом.");
      }

      return applyConfigPatch(baseConfig, {
        integrationId: typedEditor.customIntegrationId.trim() || undefined,
        method: typedEditor.customMethod.trim() || undefined,
        path: typedEditor.customPath.trim() || undefined,
        headers,
        payload,
      });
    }

    if (nodeType === "SteamAction") {
      const payload = typedEditor.steamPayloadText.trim().length > 0 ? JSON.parse(typedEditor.steamPayloadText) : undefined;
      if (payload !== undefined && !isRecord(payload)) {
        throw new Error("payload должен быть JSON-объектом.");
      }

      const delaySecondsRaw = typedEditor.steamDelaySeconds.trim();
      const delaySeconds = delaySecondsRaw.length > 0 ? Number(delaySecondsRaw) : undefined;
      if (delaySeconds !== undefined && (!Number.isFinite(delaySeconds) || delaySeconds < 0)) {
        throw new Error("delaySeconds должен быть неотрицательным числом.");
      }

      return applyConfigPatch(baseConfig, {
        action: typedEditor.steamAction.trim() || undefined,
        accountId: typedEditor.steamAccountId.trim() || undefined,
        delaySeconds: delaySeconds !== undefined ? Math.floor(delaySeconds) : undefined,
        payload,
      });
    }

    if (nodeType === "Task") {
      const payload = typedEditor.taskPayloadText.trim().length > 0 ? JSON.parse(typedEditor.taskPayloadText) : undefined;
      if (payload !== undefined && !isRecord(payload)) {
        throw new Error("payload должен быть JSON-объектом.");
      }

      const delaySecondsRaw = typedEditor.taskDelaySeconds.trim();
      const delaySeconds = delaySecondsRaw.length > 0 ? Number(delaySecondsRaw) : undefined;
      if (delaySeconds !== undefined && (!Number.isFinite(delaySeconds) || delaySeconds < 0)) {
        throw new Error("delaySeconds должен быть неотрицательным числом.");
      }

      return applyConfigPatch(baseConfig, {
        taskType: typedEditor.taskType.trim() || undefined,
        title: typedEditor.taskTitle.trim() || undefined,
        assignee: typedEditor.taskAssignee.trim() || undefined,
        delaySeconds: delaySeconds !== undefined ? Math.floor(delaySeconds) : undefined,
        payload,
      });
    }

    if (nodeType === "SendBuyerResponse") {
      return applyConfigPatch(baseConfig, {
        message: typedEditor.buyerMessage.trim() || undefined,
      });
    }

    if (nodeType === "Notify") {
      return applyConfigPatch(baseConfig, {
        message: typedEditor.notifyMessage.trim() || undefined,
      });
    }

    return { ...baseConfig };
  }, [applyConfigPatch, typedEditor]);

  const replaceSelectedNodeConfig = useCallback((nextConfig: Record<string, unknown>) => {
    if (!selectedNodeId) {
      return;
    }

    setNodes((current) => current.map((node) => {
      if (node.id !== selectedNodeId) {
        return node;
      }

      return {
        ...node,
        data: {
          ...node.data,
          config: nextConfig,
        },
      };
    }));
    setSelectedNodeConfigVersion((current) => current + 1);
  }, [selectedNodeId, setNodes]);

  const updateEdgeCondition = (edgeId: string, condition: string) => {
    setEdges((current) => current.map((edge) => {
      if (edge.id !== edgeId) {
        return edge;
      }

      const trimmed = condition.trim();
      return {
        ...edge,
        label: trimmed || undefined,
        data: {
          condition,
        },
      };
    }));
  };

  const buildValidatedDraftPayload = useCallback(() => {
    const parsedMaxSteps = Number(maxSteps);
    const parsedDuration = Number(maxDurationSeconds);
    const parsedRetries = Number(maxRetries);

    if (!Number.isFinite(parsedMaxSteps) || !Number.isFinite(parsedDuration) || !Number.isFinite(parsedRetries)) {
      throw new Error("Guard-поля должны быть числами.");
    }

    const normalizedMaxSteps = Math.floor(parsedMaxSteps);
    const normalizedDuration = Math.floor(parsedDuration);
    const normalizedRetries = Math.floor(parsedRetries);
    const nodesForSave = nodes.map((node) => {
      if (!selectedNode || node.id !== selectedNode.id) {
        return node;
      }

      const nextConfig = buildConfigFromTypedEditor(node.data.nodeType, node.data.config);
      return {
        ...node,
        data: {
          ...node.data,
          config: nextConfig,
        },
      };
    });
    const normalizedEntryNodeId = resolveStartEntryNodeId(nodesForSave, edges, entryNodeId).trim();

    validateWorkflowDraftClient({
      selectedOfferId,
      version,
      maxSteps: normalizedMaxSteps,
      maxDurationSeconds: normalizedDuration,
      maxRetries: normalizedRetries,
      nodes: nodesForSave,
      edges,
      entryNodeId: normalizedEntryNodeId,
      activeIntegrationKeys,
    });

    return buildWorkflowDraftFromEditor({
      version,
      maxSteps: normalizedMaxSteps,
      maxDurationSeconds: normalizedDuration,
      maxRetries: normalizedRetries,
      nodes: nodesForSave,
      edges,
      viewport: readViewportForSave(),
      entryNodeId: normalizedEntryNodeId || undefined,
    });
  }, [activeIntegrationKeys, buildConfigFromTypedEditor, edges, entryNodeId, maxDurationSeconds, maxRetries, maxSteps, nodes, readViewportForSave, selectedNode, selectedOfferId, version]);

  const saveDraftMutation = useMutation({
    mutationFn: async () => {
      const payload = buildValidatedDraftPayload();

      return saveOfferWorkflowDraftRequest(apiSession, projectId, selectedOfferId, payload);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["offer-workflow-draft", apiSession.baseUrl, apiSession.token, projectId, selectedOfferId],
      });
      setStatus("Workflow draft сохранен.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось сохранить draft.");
    },
  });

  const publishMutation = useMutation({
    mutationFn: async () => {
      const payload = buildValidatedDraftPayload();
      await saveOfferWorkflowDraftRequest(apiSession, projectId, selectedOfferId, payload);
      return publishOfferWorkflowRequest(apiSession, projectId, selectedOfferId);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["offer-workflow-draft", apiSession.baseUrl, apiSession.token, projectId, selectedOfferId],
      });
      await queryClient.invalidateQueries({
        queryKey: ["offer-workflow-executions", apiSession.baseUrl, apiSession.token, projectId, selectedOfferId],
      });
      setStatus("Workflow опубликован.");
    },
    onError: (error) => {
      setStatus(error instanceof Error ? error.message : "Не удалось опубликовать workflow.");
    },
  });

  const saveDraft = useCallback(() => {
    if (saveDraftMutation.isPending || !selectedOfferId) {
      return;
    }

    saveDraftMutation.mutate();
  }, [saveDraftMutation, selectedOfferId]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!canManageWorkflows) {
        return;
      }

      const metaOrCtrl = event.metaKey || event.ctrlKey;
      if (metaOrCtrl && event.key.toLowerCase() === "s") {
        event.preventDefault();
        saveDraft();
        return;
      }

      if (isTypingTarget(event.target)) {
        return;
      }

      if (event.key === "Delete" || event.key === "Backspace") {
        event.preventDefault();
        if (selectedEdgeId) {
          removeSelectedEdge();
        } else if (selectedNodeId) {
          removeSelectedNode();
        }
        return;
      }

      if (event.key.toLowerCase() === "f") {
        event.preventDefault();
        handleCenterCanvas();
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [canManageWorkflows, handleCenterCanvas, removeSelectedEdge, removeSelectedNode, saveDraft, selectedEdgeId, selectedNodeId]);

  const filteredNodeCatalog = useMemo(() => {
    const query = nodeSearch.trim().toLowerCase();
    const bySearch = workflowNodeCatalog.filter((node) => (query.length === 0
      || node.label.toLowerCase().includes(query)
      || node.type.toLowerCase().includes(query)
      || node.description.toLowerCase().includes(query)));
    const hasIntegrationSnapshot = Boolean(integrationsStatusQuery.data);

    return bySearch.filter((node) => !node.integrationKey
      || !hasIntegrationSnapshot
      || activeIntegrationKeys.has(node.integrationKey));
  }, [activeIntegrationKeys, integrationsStatusQuery.data, nodeSearch]);

  const filteredStartNodes = useMemo(
    () => filteredNodeCatalog.filter((item) => item.group === "start"),
    [filteredNodeCatalog],
  );
  const filteredBaseNodes = useMemo(
    () => filteredNodeCatalog.filter((item) => item.group === "base"),
    [filteredNodeCatalog],
  );
  const filteredIntegrationNodes = useMemo(
    () => filteredNodeCatalog.filter((item) => item.group === "integration"),
    [filteredNodeCatalog],
  );

  const isPaletteNodeDisabled = useCallback((nodeItem: WorkflowNodeCatalogItem) => {
    if (nodeItem.integrationKey && !activeIntegrationKeys.has(nodeItem.integrationKey)) {
      return true;
    }

    if (workflowStartNodeTypes.includes(nodeItem.type) && nodes.some((node) => node.data.nodeType === nodeItem.type)) {
      return true;
    }

    return false;
  }, [activeIntegrationKeys, nodes]);

  const readPaletteNodeDisabledHint = useCallback((nodeItem: WorkflowNodeCatalogItem) => {
    if (nodeItem.integrationKey && !activeIntegrationKeys.has(nodeItem.integrationKey)) {
      return `Требуется активная интеграция: ${nodeItem.integrationKey}.`;
    }

    if (workflowStartNodeTypes.includes(nodeItem.type) && nodes.some((node) => node.data.nodeType === nodeItem.type)) {
      return "Стартовая node этого типа уже добавлена.";
    }

    return "";
  }, [activeIntegrationKeys, nodes]);

  const executions: WorkflowExecution[] = executionsQuery.data ?? [];

  const applyTypedConfig = () => {
    if (!selectedNode) {
      return;
    }

    try {
      const nodeType = selectedNode.data.nodeType;
      const nextConfig = buildConfigFromTypedEditor(nodeType, selectedNode.data.config);
      replaceSelectedNodeConfig(nextConfig);

      setTypedEditorError("");
      setStatus(`Поля node ${nodeType} обновлены.`);
    } catch (error) {
      setTypedEditorError(error instanceof Error ? error.message : "Не удалось применить typed-настройки.");
    }
  };

  const applyAdvancedJson = () => {
    if (!selectedNode) {
      return;
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(advancedJsonText);
    } catch {
      setAdvancedJsonError("Advanced JSON должен быть валидным JSON.");
      return;
    }

    if (!isRecord(parsed)) {
      setAdvancedJsonError("Advanced JSON должен быть JSON-объектом.");
      return;
    }

    setAdvancedJsonError("");
    replaceSelectedNodeConfig({ ...parsed });
    setStatus("Advanced JSON применен.");
  };

  if (!canManageWorkflows) {
    return (
      <div className="panel-card page-stack" data-testid="project-workflows-panel-no-access">
        <h2>Workflows</h2>
        <p className="route-error">
          У текущей роли нет permission `project.workflows.manage`.
        </p>
      </div>
    );
  }

  return (
    <div className="page-stack" data-testid="project-workflows-panel">
      <header className="page-section-header">
        <h2>Workflows</h2>
        <p>Comfy-sync редактор: общий layout, publish и история запусков.</p>
      </header>

      <section className="panel-card workflow-command-bar">
        <div className="workflow-command-main">
          <label className="field workflow-field-grow">
            <span>Offer</span>
            <select
              className="input"
              value={selectedOfferId}
              onChange={(event) => {
                setSelectedOfferId(event.target.value);
                setSelectedNodeId("");
                setSelectedEdgeId("");
                setEntryNodeId("");
              }}
            >
              {offers.map((offer) => (
                <option key={offer.id} value={offer.id}>
                  {offer.name} ({offer.status})
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Version</span>
            <input className="input" value={version} onChange={(event) => setVersion(event.target.value)} />
          </label>
          <label className="field">
            <span>Max steps</span>
            <input className="input" value={maxSteps} onChange={(event) => setMaxSteps(event.target.value)} />
          </label>
          <label className="field">
            <span>Max duration</span>
            <input className="input" value={maxDurationSeconds} onChange={(event) => setMaxDurationSeconds(event.target.value)} />
          </label>
          <label className="field">
            <span>Max retries</span>
            <input className="input" value={maxRetries} onChange={(event) => setMaxRetries(event.target.value)} />
          </label>
          <label className="field">
            <span>Preset pack</span>
            <select className="input" value={selectedPresetId} onChange={(event) => setSelectedPresetId(event.target.value)}>
              {workflowPresets.map((preset) => (
                <option key={preset.id} value={preset.id}>{preset.label}</option>
              ))}
            </select>
            <small className="workflow-field-hint">
              {workflowPresets.find((preset) => preset.id === selectedPresetId)?.description ?? "Выберите шаблон flow."}
            </small>
          </label>
          <label className="field">
            <span>Entry point</span>
            <select
              className="input"
              value={entryNodeId}
              onChange={(event) => setWorkflowEntryNode(event.target.value)}
              disabled={startRootNodeIds.length === 0}
            >
              {startRootNodeIds.length === 0 ? <option value="">Нет стартовых root-node</option> : null}
              {startRootNodeIds.map((nodeId) => {
                const node = nodes.find((candidate) => candidate.id === nodeId);
                const nodeName = node?.data.name.trim() ? ` · ${node.data.name.trim()}` : "";
                return (
                  <option key={nodeId} value={nodeId}>
                    {node?.data.nodeType ?? "Node"}{nodeName}
                  </option>
                );
              })}
            </select>
            <small className="workflow-field-hint">Точка входа должна быть стартовой node (Purchase/Message/Review) без входящих ребер.</small>
          </label>
        </div>

        <div className="workflow-command-actions">
          <button type="button" className="button button-ghost" disabled={!selectedOfferId || !selectedPresetId} onClick={() => applyPreset(selectedPresetId)}>
            Применить preset
          </button>
          <button type="button" className="button button-ghost" disabled={!selectedOfferId} onClick={exportFlow}>
            Экспорт flow
          </button>
          <button type="button" className="button button-ghost" disabled={!selectedOfferId} onClick={openImportDialog}>
            Импорт flow
          </button>
          <button type="button" className="button button-primary" disabled={saveDraftMutation.isPending || !selectedOfferId} onClick={saveDraft}>
            Сохранить draft (Ctrl/Cmd+S)
          </button>
          <button type="button" className="button button-ghost" disabled={publishMutation.isPending || !selectedOfferId} onClick={() => publishMutation.mutate()}>
            Publish
          </button>
          <button type="button" className="button button-ghost" disabled={!selectedOfferId} onClick={() => setIsHistoryOpen(true)}>
            История запусков
          </button>
        </div>
        <input
          ref={importFileInputRef}
          type="file"
          accept="application/json,.json"
          className="visually-hidden"
          onChange={async (event) => {
            const file = event.currentTarget.files?.[0];
            if (!file) {
              return;
            }

            try {
              await importFlowFromFile(file);
            } catch (error) {
              setStatus(error instanceof Error ? error.message : "Не удалось импортировать flow.");
            } finally {
              event.currentTarget.value = "";
            }
          }}
        />
      </section>

      {offersQuery.isPending ? <p className="route-hint">Загружаем offers...</p> : null}
      {offersQuery.error ? (
        <p className="route-error">{offersQuery.error instanceof Error ? offersQuery.error.message : "Не удалось загрузить offers."}</p>
      ) : null}
      {!offersQuery.isPending && !offersQuery.error && offers.length === 0 ? (
        <section className="panel-card">
          <p className="route-hint">Сначала создайте Offer во вкладке Offers.</p>
        </section>
      ) : null}

      {selectedOfferId ? (
        <section className="workflow-workspace">
          <aside className="panel-card workflow-dock">
            <div className="workflow-dock-header">
              <h3>Node palette</h3>
              <p>Поиск и добавление блоков.</p>
            </div>
            <label className="field">
              <span>Поиск ноды</span>
              <input className="input" placeholder="Condition, http, notify..." value={nodeSearch} onChange={(event) => setNodeSearch(event.target.value)} />
            </label>
              <div className="workflow-node-palette">
                {filteredStartNodes.length > 0 ? <p className="route-hint">Start nodes</p> : null}
                {filteredStartNodes.map((nodeItem) => {
                  const disabled = isPaletteNodeDisabled(nodeItem);
                  const disabledHint = readPaletteNodeDisabledHint(nodeItem);
                  return (
                    <button
                      key={nodeItem.type}
                      type="button"
                      className="workflow-node-palette-item"
                      onClick={() => appendNode(nodeItem.type)}
                      disabled={disabled}
                      title={disabledHint || undefined}
                    >
                      <strong>{nodeItem.label}</strong>
                      <span>{nodeItem.description}</span>
                      {disabledHint ? <span>{disabledHint}</span> : null}
                    </button>
                  );
                })}
                {filteredBaseNodes.length > 0 ? <p className="route-hint">Base nodes</p> : null}
                {filteredBaseNodes.map((nodeItem) => {
                  const disabled = isPaletteNodeDisabled(nodeItem);
                  const disabledHint = readPaletteNodeDisabledHint(nodeItem);
                  return (
                    <button
                      key={nodeItem.type}
                      type="button"
                      className="workflow-node-palette-item"
                      onClick={() => appendNode(nodeItem.type)}
                      disabled={disabled}
                      title={disabledHint || undefined}
                    >
                      <strong>{nodeItem.label}</strong>
                      <span>{nodeItem.description}</span>
                      {disabledHint ? <span>{disabledHint}</span> : null}
                    </button>
                  );
                })}
                {filteredIntegrationNodes.length > 0 ? <p className="route-hint">Integration nodes</p> : null}
                {filteredIntegrationNodes.map((nodeItem) => {
                  const disabled = isPaletteNodeDisabled(nodeItem);
                  const disabledHint = readPaletteNodeDisabledHint(nodeItem);
                  return (
                    <button
                      key={nodeItem.type}
                      type="button"
                      className="workflow-node-palette-item"
                      onClick={() => appendNode(nodeItem.type)}
                      disabled={disabled}
                      title={disabledHint || undefined}
                    >
                      <strong>{nodeItem.label}</strong>
                      <span>{nodeItem.description}</span>
                      {nodeItem.integrationKey ? <span>integration: {nodeItem.integrationKey}</span> : null}
                      {disabledHint ? <span>{disabledHint}</span> : null}
                    </button>
                  );
                })}
                {filteredNodeCatalog.length === 0 ? <p className="route-hint">Ничего не найдено.</p> : null}
              </div>
            <div className="workflow-dock-footer">
              <p className="route-hint">Hotkeys: `Del`, `F`, `Ctrl/Cmd+S`.</p>
            </div>
          </aside>

          <article className="panel-card workflow-canvas-panel">
            <div className="workflow-canvas-header">
              <h3>Workflow canvas</h3>
              <div className="inline-actions">
                <button
                  type="button"
                  className="button button-ghost"
                  onClick={handleCenterCanvas}
                >
                  К центру (F)
                </button>
                <button type="button" className="button button-ghost" disabled={!selectedEdgeId} onClick={removeSelectedEdge}>
                  Удалить edge
                </button>
                <button type="button" className="button button-ghost" disabled={!selectedNodeId} onClick={removeSelectedNode}>
                  Удалить node
                </button>
                <button type="button" className="button button-ghost" onClick={() => setIsMiniMapVisible((current) => !current)}>
                  {isMiniMapVisible ? "Скрыть миникарту" : "Показать миникарту"}
                </button>
              </div>
            </div>

            <div className="workflow-canvas workspace">
              <ReactFlow
                nodes={nodes}
                edges={edges}
                nodeTypes={workflowNodeRenderers}
                defaultEdgeOptions={defaultEdgeOptions}
                onlyRenderVisibleElements={false}
                elementsSelectable={false}
                nodesConnectable
                nodesDraggable
                edgesReconnectable={false}
                nodesFocusable={false}
                edgesFocusable={false}
                disableKeyboardA11y
                autoPanOnConnect={false}
                autoPanOnNodeDrag={false}
                connectOnClick={false}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onConnect={onConnect}
                onInit={setFlowInstance}
                onMoveEnd={handleCanvasMoveEnd}
                onNodeClick={handleCanvasNodeClick}
                onEdgeClick={handleCanvasEdgeClick}
                onPaneClick={handleCanvasPaneClick}
                fitView
              >
                {isMiniMapVisible ? <MiniMap pannable zoomable /> : null}
                <Controls />
                <Background variant={BackgroundVariant.Dots} gap={24} size={1.2} />
              </ReactFlow>
            </div>
          </article>

          <aside className="panel-card workflow-inspector">
            <div className="workflow-inspector-header">
              <h3>Inspector</h3>
              <p>Node/edge настройки и typed-конфиг.</p>
            </div>

            {selectedNode ? (
              <div className="page-stack">
                <label className="field">
                  <span>Node type</span>
                  <select
                    className="input"
                    value={selectedNode.data.nodeType}
                    onChange={(event) => {
                      updateSelectedNodeMeta({ nodeType: event.target.value as WorkflowNode["type"] });
                      setStatus("Тип node обновлен.");
                    }}
                  >
                    {workflowNodeTypes.map((nodeType) => (
                      <option key={nodeType} value={nodeType}>{nodeType}</option>
                    ))}
                  </select>
                </label>

                <label className="field">
                  <span>Name (optional)</span>
                  <input className="input" value={selectedNode.data.name} onChange={(event) => updateSelectedNodeMeta({ name: event.target.value })} />
                </label>

                <div className="inline-actions">
                  <button
                    type="button"
                    className="button button-ghost"
                    disabled={!selectedNodeIsStartRoot || selectedNode.id === entryNodeId}
                    onClick={() => setWorkflowEntryNode(selectedNode.id)}
                  >
                    Сделать entry point
                  </button>
                </div>
                {!selectedNodeIsStartRoot ? (
                  <p className="route-hint">Entry point можно назначить только root-node типа Purchase/Message/Review.</p>
                ) : null}
                {selectedNode.id === entryNodeId ? <p className="route-hint">Эта node является точкой входа workflow.</p> : null}

                <details className="details-block" open>
                  <summary>Поля node (inputs/outputs)</summary>
                  <div className="page-stack">
                    {nodePortsByType[selectedNode.data.nodeType].inputs.length > 0 ? (
                      <div className="page-stack">
                        <strong>Inputs</strong>
                        {nodePortsByType[selectedNode.data.nodeType].inputs.map((port) => (
                          <article key={`port-input-${port.id}`} className="stacked-block">
                            <strong>{port.label}</strong>
                            <p className="route-hint">{port.description}</p>
                            {port.valueType ? <p className="route-hint">Type: {port.valueType}</p> : null}
                          </article>
                        ))}
                      </div>
                    ) : (
                      <p className="route-hint">Inputs: нет.</p>
                    )}
                    {nodePortsByType[selectedNode.data.nodeType].outputs.length > 0 ? (
                      <div className="page-stack">
                        <strong>Outputs</strong>
                        {nodePortsByType[selectedNode.data.nodeType].outputs.map((port) => (
                          <article key={`port-output-${port.id}`} className="stacked-block">
                            <strong>{port.label}</strong>
                            <p className="route-hint">{port.description}</p>
                            {port.valueType ? <p className="route-hint">Type: {port.valueType}</p> : null}
                          </article>
                        ))}
                      </div>
                    ) : (
                      <p className="route-hint">Outputs: нет.</p>
                    )}
                  </div>
                </details>

                {selectedNode.data.nodeType === "Condition" ? (
                  <>
                    <label className="field">
                      <span>field</span>
                      <input
                        className="input"
                        value={typedEditor.conditionField}
                        onChange={(event) => setTypedEditor((prev) => ({ ...prev, conditionField: event.target.value }))}
                        placeholder={readFieldDescriptor("Condition", "field")?.placeholder ?? "platform"}
                      />
                      <small className="workflow-field-hint">{readFieldHintText("Condition", "field")}</small>
                    </label>
                    <label className="field">
                      <span>equals</span>
                      <input
                        className="input"
                        value={typedEditor.conditionEquals}
                        onChange={(event) => setTypedEditor((prev) => ({ ...prev, conditionEquals: event.target.value }))}
                        placeholder={readFieldDescriptor("Condition", "equals")?.placeholder ?? "steam"}
                      />
                      <small className="workflow-field-hint">{readFieldHintText("Condition", "equals")}</small>
                    </label>
                  </>
                ) : null}

                {selectedNode.data.nodeType === "SetVariables" ? (
                  <label className="field">
                    <span>values (JSON object)</span>
                    <textarea className="input" rows={8} value={typedEditor.setVariablesText} onChange={(event) => setTypedEditor((prev) => ({ ...prev, setVariablesText: event.target.value }))} />
                    <small className="workflow-field-hint">{readFieldHintText("SetVariables", "values")}</small>
                  </label>
                ) : null}

                {selectedNode.data.nodeType === "SelectAccountPriorityFallback" ? (
                  <label className="field">
                    <span>platform</span>
                    <input
                      className="input"
                      value={typedEditor.selectPlatform}
                      onChange={(event) => setTypedEditor((prev) => ({ ...prev, selectPlatform: event.target.value }))}
                      placeholder={readFieldDescriptor("SelectAccountPriorityFallback", "platform")?.placeholder ?? "steam"}
                    />
                    <small className="workflow-field-hint">{readFieldHintText("SelectAccountPriorityFallback", "platform")}</small>
                  </label>
                ) : null}

                {selectedNode.data.nodeType === "InvokeWorkerAction" ? (
                  <label className="field">
                    <span>action</span>
                    <input
                      className="input"
                      value={typedEditor.workerAction}
                      onChange={(event) => setTypedEditor((prev) => ({ ...prev, workerAction: event.target.value }))}
                      placeholder={readFieldDescriptor("InvokeWorkerAction", "action")?.placeholder ?? "ext.integration.steam.jobs"}
                    />
                    <small className="workflow-field-hint">{readFieldHintText("InvokeWorkerAction", "action")}</small>
                  </label>
                ) : null}

                {selectedNode.data.nodeType === "InvokeCustomHttp" ? (
                  <>
                    <label className="field">
                      <span>integrationId</span>
                      <input
                        className="input"
                        value={typedEditor.customIntegrationId}
                        onChange={(event) => setTypedEditor((prev) => ({ ...prev, customIntegrationId: event.target.value }))}
                        placeholder={readFieldDescriptor("InvokeCustomHttp", "integrationId")?.placeholder ?? "GUID"}
                      />
                      <small className="workflow-field-hint">{readFieldHintText("InvokeCustomHttp", "integrationId")}</small>
                    </label>
                    <label className="field">
                      <span>method</span>
                      <input
                        className="input"
                        value={typedEditor.customMethod}
                        onChange={(event) => setTypedEditor((prev) => ({ ...prev, customMethod: event.target.value }))}
                        placeholder={readFieldDescriptor("InvokeCustomHttp", "method")?.placeholder ?? "POST"}
                      />
                      <small className="workflow-field-hint">{readFieldHintText("InvokeCustomHttp", "method")}</small>
                    </label>
                    <label className="field">
                      <span>path</span>
                      <input
                        className="input"
                        value={typedEditor.customPath}
                        onChange={(event) => setTypedEditor((prev) => ({ ...prev, customPath: event.target.value }))}
                        placeholder={readFieldDescriptor("InvokeCustomHttp", "path")?.placeholder ?? "/orders/fulfill"}
                      />
                      <small className="workflow-field-hint">{readFieldHintText("InvokeCustomHttp", "path")}</small>
                    </label>
                    <label className="field">
                      <span>headers (JSON object)</span>
                      <textarea className="input" rows={4} value={typedEditor.customHeadersText} onChange={(event) => setTypedEditor((prev) => ({ ...prev, customHeadersText: event.target.value }))} />
                      <small className="workflow-field-hint">{readFieldHintText("InvokeCustomHttp", "headers")}</small>
                    </label>
                    <label className="field">
                      <span>payload (JSON object)</span>
                      <textarea className="input" rows={6} value={typedEditor.customPayloadText} onChange={(event) => setTypedEditor((prev) => ({ ...prev, customPayloadText: event.target.value }))} />
                      <small className="workflow-field-hint">{readFieldHintText("InvokeCustomHttp", "payload")}</small>
                    </label>
                  </>
                ) : null}

                {selectedNode.data.nodeType === "SteamAction" ? (
                  <>
                    <label className="field">
                      <span>action</span>
                      <input className="input" value={typedEditor.steamAction} onChange={(event) => setTypedEditor((prev) => ({ ...prev, steamAction: event.target.value }))} placeholder="change-password" />
                      <small className="workflow-field-hint">{readFieldHintText("SteamAction", "action")}</small>
                    </label>
                    <label className="field">
                      <span>accountId</span>
                      <input className="input" value={typedEditor.steamAccountId} onChange={(event) => setTypedEditor((prev) => ({ ...prev, steamAccountId: event.target.value }))} placeholder="GUID" />
                      <small className="workflow-field-hint">{readFieldHintText("SteamAction", "accountId")}</small>
                    </label>
                    <label className="field">
                      <span>delaySeconds</span>
                      <input className="input" value={typedEditor.steamDelaySeconds} onChange={(event) => setTypedEditor((prev) => ({ ...prev, steamDelaySeconds: event.target.value }))} placeholder="10800" />
                      <small className="workflow-field-hint">{readFieldHintText("SteamAction", "delaySeconds")}</small>
                    </label>
                    <label className="field">
                      <span>payload (JSON object)</span>
                      <textarea className="input" rows={6} value={typedEditor.steamPayloadText} onChange={(event) => setTypedEditor((prev) => ({ ...prev, steamPayloadText: event.target.value }))} />
                      <small className="workflow-field-hint">{readFieldHintText("SteamAction", "payload")}</small>
                    </label>
                  </>
                ) : null}

                {selectedNode.data.nodeType === "Task" ? (
                  <>
                    <label className="field">
                      <span>taskType</span>
                      <input className="input" value={typedEditor.taskType} onChange={(event) => setTypedEditor((prev) => ({ ...prev, taskType: event.target.value }))} placeholder="steam.change-password" />
                      <small className="workflow-field-hint">{readFieldHintText("Task", "taskType")}</small>
                    </label>
                    <label className="field">
                      <span>title</span>
                      <input className="input" value={typedEditor.taskTitle} onChange={(event) => setTypedEditor((prev) => ({ ...prev, taskTitle: event.target.value }))} placeholder="Сменить пароль через 3 часа" />
                      <small className="workflow-field-hint">{readFieldHintText("Task", "title")}</small>
                    </label>
                    <label className="field">
                      <span>assignee</span>
                      <input className="input" value={typedEditor.taskAssignee} onChange={(event) => setTypedEditor((prev) => ({ ...prev, taskAssignee: event.target.value }))} placeholder="support-team" />
                      <small className="workflow-field-hint">{readFieldHintText("Task", "assignee")}</small>
                    </label>
                    <label className="field">
                      <span>delaySeconds</span>
                      <input className="input" value={typedEditor.taskDelaySeconds} onChange={(event) => setTypedEditor((prev) => ({ ...prev, taskDelaySeconds: event.target.value }))} placeholder="10800" />
                      <small className="workflow-field-hint">{readFieldHintText("Task", "delaySeconds")}</small>
                    </label>
                    <label className="field">
                      <span>payload (JSON object)</span>
                      <textarea className="input" rows={6} value={typedEditor.taskPayloadText} onChange={(event) => setTypedEditor((prev) => ({ ...prev, taskPayloadText: event.target.value }))} />
                      <small className="workflow-field-hint">{readFieldHintText("Task", "payload")}</small>
                    </label>
                  </>
                ) : null}

                {selectedNode.data.nodeType === "SendBuyerResponse" ? (
                  <label className="field">
                    <span>message</span>
                    <textarea className="input" rows={4} value={typedEditor.buyerMessage} onChange={(event) => setTypedEditor((prev) => ({ ...prev, buyerMessage: event.target.value }))} />
                    <small className="workflow-field-hint">{readFieldHintText("SendBuyerResponse", "message")}</small>
                  </label>
                ) : null}

                {selectedNode.data.nodeType === "Notify" ? (
                  <label className="field">
                    <span>message</span>
                    <textarea className="input" rows={4} value={typedEditor.notifyMessage} onChange={(event) => setTypedEditor((prev) => ({ ...prev, notifyMessage: event.target.value }))} />
                    <small className="workflow-field-hint">{readFieldHintText("Notify", "message")}</small>
                  </label>
                ) : null}

                <div className="inline-actions">
                  <button type="button" className="button button-ghost" onClick={applyTypedConfig}>Применить поля</button>
                </div>
                {typedEditorError ? <p className="route-error">{typedEditorError}</p> : null}

                <details
                  className="details-block"
                  open={isAdvancedJsonOpen}
                  onToggle={(event) => {
                    const isOpen = event.currentTarget.open;
                    setIsAdvancedJsonOpen(isOpen);
                    if (isOpen && selectedNode && advancedJsonText.length === 0) {
                      setAdvancedJsonText(JSON.stringify(selectedNode.data.config, null, 2));
                    }
                  }}
                >
                  <summary>Advanced JSON (config object)</summary>
                  <div className="page-stack">
                    <label className="field">
                      <span>JSON</span>
                      <textarea className="input" rows={10} value={advancedJsonText} onChange={(event) => setAdvancedJsonText(event.target.value)} />
                    </label>
                    <div className="inline-actions">
                      <button type="button" className="button button-ghost" onClick={applyAdvancedJson}>Применить JSON</button>
                    </div>
                    {advancedJsonError ? <p className="route-error">{advancedJsonError}</p> : null}
                  </div>
                </details>
              </div>
            ) : selectedEdge ? (
              <div className="page-stack">
                <p className="route-hint">
                  Edge: {selectedEdge.source}:{selectedEdge.sourceHandle ?? "*"} → {selectedEdge.target}:{selectedEdge.targetHandle ?? "*"}
                </p>
                <label className="field">
                  <span>Condition (optional)</span>
                  <input className="input" value={selectedEdge.data?.condition ?? ""} onChange={(event) => updateEdgeCondition(selectedEdge.id, event.target.value)} placeholder="condition:check == true" />
                </label>
                <button type="button" className="button button-ghost" onClick={removeSelectedEdge}>Удалить edge</button>
              </div>
            ) : (
              <p className="route-hint">Выберите node или edge на схеме для редактирования.</p>
            )}
          </aside>
        </section>
      ) : null}

      {draftQuery.error ? (
        <p className="route-error">{draftQuery.error instanceof Error ? draftQuery.error.message : "Не удалось загрузить draft workflow."}</p>
      ) : null}

      <p className="route-hint">{status}</p>

      {isHistoryOpen ? (
        <section className="workflow-history-layer" role="presentation">
          <button type="button" className="workflow-history-backdrop" aria-label="Закрыть историю запусков" onClick={() => setIsHistoryOpen(false)} />
          <aside className="workflow-history-drawer" role="dialog" aria-modal="true" aria-label="История запусков workflow">
            <header className="workflow-history-header">
              <div>
                <h3>Execution history</h3>
                <p>Последние 50 запусков текущего Offer.</p>
              </div>
              <div className="inline-actions">
                <button type="button" className="button button-ghost" onClick={() => executionsQuery.refetch()} disabled={executionsQuery.isFetching}>
                  Обновить
                </button>
                <button type="button" className="button button-ghost" onClick={() => setIsHistoryOpen(false)}>
                  Закрыть
                </button>
              </div>
            </header>

            {executionsQuery.isPending ? <p className="route-hint">Загружаем executions...</p> : null}
            {executionsQuery.error ? (
              <p className="route-error">{executionsQuery.error instanceof Error ? executionsQuery.error.message : "Не удалось загрузить executions."}</p>
            ) : null}
            {!executionsQuery.isPending && !executionsQuery.error && executions.length === 0 ? <p className="route-hint">История запусков пуста.</p> : null}
            {executions.length > 0 ? (
              <div className="page-stack">
                {executions.map((execution) => (
                  <details key={execution.id} className="details-block">
                    <summary>{execution.status} · order: {execution.sourceOrderId} · v{execution.workflowVersion}</summary>
                    <dl className="kv-list">
                      <div>
                        <dt>Started</dt>
                        <dd>{new Date(execution.startedAtUtc).toLocaleString()}</dd>
                      </div>
                      <div>
                        <dt>Finished</dt>
                        <dd>{execution.finishedAtUtc ? new Date(execution.finishedAtUtc).toLocaleString() : "-"}</dd>
                      </div>
                      <div>
                        <dt>Error</dt>
                        <dd>{execution.lastError || "-"}</dd>
                      </div>
                    </dl>
                    {execution.steps.length > 0 ? (
                      <div className="page-stack">
                        {execution.steps.map((step) => (
                          <article key={`${execution.id}-${step.stepIndex}-${step.nodeId}`} className="stacked-block">
                            <strong>{step.stepIndex}. {step.nodeType}</strong>
                            <p className="route-hint">nodeId: {step.nodeId} · status: {step.status}</p>
                            {step.error ? <p className="route-error">{step.error}</p> : null}
                          </article>
                        ))}
                      </div>
                    ) : (
                      <p className="route-hint">Шаги пока не зафиксированы.</p>
                    )}
                  </details>
                ))}
              </div>
            ) : null}
          </aside>
        </section>
      ) : null}
    </div>
  );
}
